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

using System.Reactive.Disposables;
using System.Threading.Tasks;
using MDHandbookApp.Forms.Actions;
using MDHandbookApp.Forms.Services;
using Prism.Mvvm;
using Prism.Navigation;


namespace MDHandbookApp.Forms.ViewModels
{
    public class ViewModelBase : BindableBase, INavigationAware
    {
        protected readonly CompositeDisposable subscriptionDisposibles = new CompositeDisposable();

        protected ILogService _logService;
        protected INavigationService _navigationService;
        protected IReduxService _reduxService;
        protected IServerActionCreators _serverActionCreators;


        public ViewModelBase(
            ILogService logService = null,
            INavigationService navigationService = null,
            IReduxService reduxService = null,
            IServerActionCreators serverActionCreators = null)
        {
            _logService = logService;
            _navigationService = navigationService;
            _reduxService = reduxService;
            _serverActionCreators = serverActionCreators;
        }

        protected virtual async Task navigateToMainPage()
        {
            await _navigationService.NavigateAsync(Constants.MainPageAbsUrl, animated: true);
        }

        protected virtual async Task navigateToMainPageRel()
        {
            await _navigationService.NavigateAsync(Constants.MainPageRelUrl, animated: true);
        }

        public virtual void OnNavigatedFrom(NavigationParameters parameters)
        {
            subscriptionDisposibles.Clear();
        }

        public virtual void OnNavigatedTo(NavigationParameters parameters)
        {
        }

        protected virtual void setupObservables() { }

        protected virtual void setupSubscriptions() { }
    }
}
