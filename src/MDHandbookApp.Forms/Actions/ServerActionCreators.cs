﻿//
//  Copyright 2016  R. Stanley Hum <r.stanley.hum@gmail.com>
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using MDHandbookApp.Forms.States;
using MDHandbookApp.Forms.Utilities;
using MDHandbookAppService.Common.Models.RequestMessages;
using MDHandbookAppService.Common.Models.Utility;
using Redux;
using System.Threading.Tasks;
using MDHandbookApp.Forms.Services;


namespace MDHandbookApp.Forms.Actions
{

    public class ServerActionCreators : IServerActionCreators
    {
        private IReduxService _reduxService;
        private IMobileService _mobileService;
        private ILogService _logService;

        public ServerActionCreators(
            ILogService logService,
            IReduxService reduxService,
            IMobileService mobileService)
        {
            _logService = logService;
            _reduxService = reduxService;
            _mobileService = mobileService;
        }

        private bool httpClientAvailable = true;

        private bool loggingIn = false;
        private bool updatingData = false;
        private bool verifyingLicenceKey = false;
        private bool refreshingToken = false;
        private bool postingUpdateData = false;
        private bool uploadingAppLog = false;
        private bool fullupdating = false;
        private bool resettingUpdates = false;


        public AsyncActionsCreator<AppState> LoginAction(LoginProviders provider)
        {
            return async (dispatch, getState) => {
                await doLogin(dispatch, getState, provider);
            };
        }     

        public AsyncActionsCreator<AppState> VerifyLicenceKeyAction(string licencekey)
        {
            return async (dispatch, getState) => {
                await doVerifyLicenceKey(dispatch, getState, licencekey);
            };
        }

        public AsyncActionsCreator<AppState> RefreshTokenAction()
        {
            return async (dispatch, getState) => {
                await doRefreshToken(dispatch, getState);
            };
        }

        public AsyncActionsCreator<AppState> LogoutAction()
        {
            return async (dispatch, getState) => {
                await doLogout(dispatch, getState);
            };
        }

        public AsyncActionsCreator<AppState> ResetLicenceKeyAction()
        {
            return async (dispatch, getState) => {
                await doResetLicenceKey(dispatch, getState);
            };
        }



        public AsyncActionsCreator<AppState> FullUpdateAction()
        {
            return async (dispatch, getState) => {

                if (!getState().CurrentState.IsLicensed || fullupdating)
                {
                    return;
                }

                fullupdating = true;

                await doPostUpdates(dispatch, getState);
                
                await doGetUpdates(dispatch, getState);

                await doPostUpdates(dispatch, getState);

                await doUploadAppLog(dispatch, getState);

                //await _offlineService.SaveAppState(_reduxService.Store.GetState());

                fullupdating = false;
            };
        }


        public AsyncActionsCreator<AppState> GetUpdatesAction()
        {
            return async (dispatch, getState) => {
                await doGetUpdates(dispatch, getState);
            };
        }

        public AsyncActionsCreator<AppState> UploadAppLogAction()
        {
            return async (dispatch, getState) => {
                await doUploadAppLog(dispatch, getState);
            };
        }

        public AsyncActionsCreator<AppState> PostUpdatesAction()
        {
            return async (dispatch, getState) => {
                await doPostUpdates(dispatch, getState);
            };
        }



        public AsyncActionsCreator<AppState> RefreshContentsAction()
        {
            return async (dispatch, getState) => {
                await doRefreshContents(dispatch, getState);
            };
        }
         



        private async Task doLogin(Dispatcher dispatch, Func<AppState> getState, LoginProviders provider)
        {
            if (!httpClientAvailable || loggingIn)
            {
                return;
            }

            httpClientAvailable = false;
            loggingIn = true;

            dispatch(new SetIsNetworkBusyAction());

            
            var success = await _mobileService.Authenticate(provider);

            if (success)
            {
                var userId = _mobileService.GetUserId();
                var token = _mobileService.GetToken();
                dispatch(new LoginAction {
                    UserId = userId,
                    AuthToken = token
                });
                _mobileService.SetAzureUserCredentials(userId, token);

                await doVerifyLicenceKey(dispatch, getState, getState().CurrentState.LicenceKey);
            }
            else
            {
                dispatch(new LogoutAction());
            }

            dispatch(new ClearIsNetworkBusyAction());
            loggingIn = false;
            httpClientAvailable = true;
        }

        private async Task doVerifyLicenceKey(Dispatcher dispatch, Func<AppState> getState, string licencekey = null)
        {
            var candidateLicenceKey = licencekey ?? getState().CurrentState.LicenceKey;

            if (string.IsNullOrEmpty(candidateLicenceKey))
                return;
            
            if (!getState().CurrentState.IsLoggedIn || verifyingLicenceKey || !httpClientAvailable)
                return;

            httpClientAvailable = false;
            verifyingLicenceKey = true;

            dispatch(new SetIsNetworkBusyAction());

            bool success = false;

            try
            {
                VerifyLicenceKeyMessage vlkm = new VerifyLicenceKeyMessage { LicenceKey = candidateLicenceKey };
                success = await _mobileService.VerifyLicenceKey(vlkm);

                if (success)
                {
                    dispatch(new SetLicensedAction());
                }
                else
                {
                    dispatch(new SetHasLicensedErrorAction());
                }

            }
            catch (Exception ex)
            {
                if (ex is ServerExceptions.NetworkFailure)
                {
                    // Do Nothing extra
                }
                else if (ex is ServerExceptions.ActionFailure)
                {
                    dispatch(new SetHasLicensedErrorAction());
                }
                else if (ex is ServerExceptions.Unauthorized)
                {
                    dispatch(new SetHasUnauthorizedErrorAction());
                }
                else if (ex is ServerExceptions.UnknownFailure)
                {
                    dispatch(new SetHasLicensedErrorAction());
                    if (ex.InnerException != null)
                    {
                    }

                }
                else
                {
                    dispatch(new SetHasLicensedErrorAction());
                }
            }

            dispatch(new ClearIsNetworkBusyAction());

            verifyingLicenceKey = false;
            httpClientAvailable = true;
        }

        private async Task doRefreshToken(Dispatcher dispatch, Func<AppState> getState)
        {

            if (!getState().CurrentState.IsLoggedIn || refreshingToken)
                return;

            refreshingToken = true;

            dispatch(new SetIsNetworkBusyAction());

            string token = null;
            try
            {
                token = await _mobileService.RefreshToken();

                if (token != null)
                {
                    dispatch(new SetRefreshTokenAction { Token = token });
                    var userId = _mobileService.GetUserId();
                    _mobileService.SetAzureUserCredentials(userId, token);
                }
                else
                {
                    dispatch(new SetHasUnauthorizedErrorAction());
                }

            }
            catch (Exception ex)
            {
                token = null;
                if (ex is ServerExceptions.NetworkFailure)
                {
                    // Do Nothing extra
                }
                else if (ex is ServerExceptions.ActionFailure)
                {
                    dispatch(new SetHasUnauthorizedErrorAction());
                }
                else if (ex is ServerExceptions.Unauthorized)
                {
                    dispatch(new SetHasUnauthorizedErrorAction());
                }
                else if (ex is ServerExceptions.UnknownFailure)
                {
                    dispatch(new SetHasUnauthorizedErrorAction());
                    if (ex.InnerException != null)
                    {
                        //Log inner Exception
                    }
                }
                else
                {
                    dispatch(new SetHasUnauthorizedErrorAction());
                    //Log Unknown Exception
                }
            }

            dispatch(new ClearIsNetworkBusyAction());
            refreshingToken = false;
        }

        private async Task doLogout(Dispatcher dispatch, Func<AppState> getState)
        {
            await Task.Run(() => { dispatch(new LogoutAction()); });
        }

        private async Task doResetLicenceKey(Dispatcher dispatch, Func<AppState> getState)
        {
            await Task.Run(() => { dispatch(new ClearLicensedAction()); });
        }



        private async Task doGetUpdates(Dispatcher dispatch, Func<AppState> getState)
        {

            if (!getState().CurrentState.IsLicensed || updatingData)
            {
                return;
            }

            updatingData = true;
            dispatch(new SetIsNetworkBusyAction());

            List<ServerUpdateMessage> messages = null;
            try
            {
                messages = await _mobileService.GetUpdates();
                processServerUpdateMessages(messages);
                dispatch(new SetLastUpdateTimeAction { UpdateTime = DateTimeOffset.UtcNow });
            }
            catch (Exception ex)
            {
                if (ex is ServerExceptions.NetworkFailure)
                {
                    // Do Nothing extra
                }
                else if (ex is ServerExceptions.ActionFailure)
                {
                    // Do Nothing extra
                }
                else if (ex is ServerExceptions.Unauthorized)
                {
                    dispatch(new SetHasUnauthorizedErrorAction());
                }
                else if (ex is ServerExceptions.UnknownFailure)
                {
                    if (ex.InnerException != null)
                    {
                        //Log Inner Exception
                    }
                }
                else
                {
                    // Log Unknown exception
                }
            }
            finally
            {
                messages.Clear();
            }

            dispatch(new ClearIsNetworkBusyAction());
            updatingData = false;
        }

        private async Task doUploadAppLog(Dispatcher dispatch, Func<AppState> getState)
        {

            if (!getState().CurrentState.IsLicensed || uploadingAppLog)
            {
                return;
            }

            uploadingAppLog = true;

            dispatch(new SetIsNetworkBusyAction());

            bool success = false;

            List<AppLogItemMessage> items = new List<AppLogItemMessage>(); // TODO: _logStoreService.LogStore.ToList();

            try
            {
                success = await _mobileService.LoadAppLog(items);

                if (success)
                {
                    //_logStoreService.Clear();
                }
            }
            catch (Exception ex)
            {
                if (ex is ServerExceptions.NetworkFailure)
                {
                    // pass
                }
                else if (ex is ServerExceptions.ActionFailure)
                {

                }
                else if (ex is ServerExceptions.Unauthorized)
                {
                    dispatch(new SetHasUnauthorizedErrorAction());
                }
                else if (ex is ServerExceptions.UnknownFailure)
                {
                    if (ex.InnerException != null)
                    {
                        //Log inner Exception
                    }
                }
                else
                {
                    //Log Unknown Exception
                }
            }
            finally
            {
                items.Clear();
            }

            dispatch(new ClearIsNetworkBusyAction());
            uploadingAppLog = false;
        }

        private async Task doPostUpdates(Dispatcher dispatch, Func<AppState> getState)
        {
            if (!getState().CurrentState.IsLicensed || postingUpdateData)
            {
                return;
            }

            postingUpdateData = true;
            dispatch(new SetIsNetworkBusyAction());

            bool success = false;

            try
            {
                UpdateJsonMessage ujm = createPostUpdateJsonMessage();
                success = await _mobileService.PostUpdates(ujm);

                if(success)
                {
                    dispatch(new RemoveLocalPostUpdatesDataAction { Data = ujm });
                }
            }
            catch (Exception ex)
            {
                if (ex is ServerExceptions.NetworkFailure)
                {
                    // Do Nothing Extra
                }
                else if (ex is ServerExceptions.ActionFailure)
                {
                    // Do Nothing Extra
                }
                else if (ex is ServerExceptions.Unauthorized)
                {
                    dispatch(new SetHasUnauthorizedErrorAction());
                }
                else if (ex is ServerExceptions.UnknownFailure)
                {
                    if (ex.InnerException != null)
                    {
                        //Log inner Exception
                    }
                }
                else
                {
                    //Log Unknown Exception
                }
            }
            
            dispatch(new ClearIsNetworkBusyAction());
            postingUpdateData = false;
        }


        private async Task doRefreshContents(Dispatcher dispatch, Func<AppState> getState)
        {
            if (!getState().CurrentState.IsLicensed || resettingUpdates)
            {
                return;
            }

            resettingUpdates = true;

            dispatch(new SetIsNetworkBusyAction());
            
            bool success = false;
            try
            {
                success = await _mobileService.ResetUpdates();

                if(success)
                {
                    await _reduxService.Store.Dispatch(this.FullUpdateAction());
                }
            }
            catch (Exception ex)
            {
                if (ex is ServerExceptions.NetworkFailure)
                {
                    // Do Nothing extra
                }
                else if (ex is ServerExceptions.ActionFailure)
                {
                    // Do Nothing extra
                }
                else if (ex is ServerExceptions.Unauthorized)
                {
                    dispatch(new SetHasUnauthorizedErrorAction());
                }
                else if (ex is ServerExceptions.UnknownFailure)
                {
                    if (ex.InnerException != null)
                    {
                        //Log Inner Exception
                    }
                }
                else
                {
                    // Log Unknown exception
                }
            }

            dispatch(new ClearIsNetworkBusyAction());
            resettingUpdates = false;
        }



        private void processServerUpdateMessages(List<ServerUpdateMessage> messages)
        {
            var addFullpages = messages
                .Where(x => x.Action == ServerUpdateMessage.AddFullpageActionId)
                .Select(x => new Fullpage() { Id = x.FullPageID, Title = x.FullPageTitle, Content = new Xamarin.Forms.HtmlWebViewSource() { Html = x.FullPageContent } });
            var updateFullpageAction = new AddFullpageRangeAction { Fullpages = addFullpages.ToList() };
            _reduxService.Store.Dispatch(updateFullpageAction);

            var updatePostFullpageAction = new AddPostUpdateAddFullpageIdsRangeAction { FullpageIds = addFullpages.Select(x => x.Id).ToList() };
            _reduxService.Store.Dispatch(updatePostFullpageAction);

                
            var addBooks = messages
                .Where(x => x.Action == ServerUpdateMessage.AddBookActionId)
                .Select(x => new Book() { Id = x.BookID, Title = x.BookTitle, StartingBookpage = x.BookStartingID, OrderIndex = x.BookOrder });
            var updateBookAction = new AddBookRangeAction { Books = addBooks.ToList() };
            _reduxService.Store.Dispatch(updateBookAction);

            var updatePostBookAction = new AddPostUpdateAddBookIdsRangeAction { BookIds = addBooks.Select(x => x.Id).ToList() };
            _reduxService.Store.Dispatch(updatePostBookAction);


            var deleteFullpages = messages
                .Where(x => x.Action == ServerUpdateMessage.DeleteFullpageActionId)
                .Select(x => x.FullPageID);
            var deleteFullpageAction = new DeleteFullpageRangeAction { FullpageIds = deleteFullpages.ToList() };
            _reduxService.Store.Dispatch(deleteFullpageAction);

            var deletePostFullpageAction = new AddPostUpdateDeleteFullpageIdsRangeAction { FullpageIds = deleteFullpages.ToList() };
            _reduxService.Store.Dispatch(deletePostFullpageAction);


            var deleteBooks = messages
                .Where(x => x.Action == ServerUpdateMessage.DeleteBookActionId)
                .Select(x => x.BookID);
            var deleteBookAction = new DeleteBookRangeAction { BookIds = deleteBooks.ToList() };
            _reduxService.Store.Dispatch(deleteBookAction);

            var deletePostBookAction = new AddPostUpdateDeleteBookIdsRangeAction { BookIds = deleteBooks.ToList() };
            _reduxService.Store.Dispatch(deletePostBookAction);

            if (messages.Count != 0)
            {
                _reduxService.Store.Dispatch(new SetIsDataUpdatedAction());
            }
        }

        private UpdateJsonMessage createPostUpdateJsonMessage()
        {
            return new UpdateJsonMessage {
                AddBookItemIds = _reduxService.Store.GetState().CurrentPostUpdateState.AddedBookIds.ToList(),
                DeleteBookItemIds = _reduxService.Store.GetState().CurrentPostUpdateState.DeletedBooksIds.ToList(),
                AddFullpageItemIds = _reduxService.Store.GetState().CurrentPostUpdateState.AddedFullpagesIds.ToList(),
                DeleteFullpageItemIds = _reduxService.Store.GetState().CurrentPostUpdateState.DeletedFullpagesIds.ToList()
            };
        }
    }
}