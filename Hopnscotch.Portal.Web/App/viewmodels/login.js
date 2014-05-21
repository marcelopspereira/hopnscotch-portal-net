﻿define(['durandal/system','plugins/router', 'durandal/app', 'services/security', 'services/session', 'services/logger', 'knockout', 'knockout.validation'],
    function (system, router, app, security, session, logger, ko) {
        // Internal properties and functions
        function ExternalLoginProviderViewModel(data) {
            var self = this;

            // Data
            self.name = ko.observable(data.name);

            // Operations
            self.login = function () {
                sessionStorage["state"] = data.state;
                sessionStorage["loginUrl"] = data.url;
                sessionStorage["externalLogin"] = true;
                // IE doesn't reliably persist sessionStorage when navigating to another URL. Move sessionStorage temporarily
                // to localStorage to work around this problem.
                session.archiveSessionStorageToLocalStorage();
                window.location = data.url;
            };
        }
        // Reveal the bindable properties and functions
        var vm = {
            activate: activate,
            goBack: goBack,
            title: 'login',
            session: session,
            validationTriggered: ko.observable(false),
            userName: ko.observable("").extend({ required: true }),
            password: ko.observable("").extend({ required: true }),
            rememberMe: ko.observable(false),
            externalLoginProviders: ko.observableArray(),
            loaded: false,
            login: login,
            register: register
        };

        vm.validationErrors = ko.validation.group([vm.userName, vm.password]);
        vm.hasExternalLogin = ko.computed(function () {
            return vm.externalLoginProviders().length > 0;
        });

        return vm;

        function activate() {

            var dfd = $.Deferred();

            session.isBusy(true);

            if (!vm.loaded) {
                security.getExternalLogins(security.returnUrl, true /* generateState */)
                   .done(function (data) {
                       if (typeof (data) === "object") {
                           for (var i = 0; i < data.length; i++) {
                               vm.externalLoginProviders.push(new ExternalLoginProviderViewModel(data[i]));
                           }

                           vm.loaded = true;
                       } else {
                           logger.logError('Error loading external authentication providers.', null, system.getModuleId(vm), true);
                           vm.loaded = false;
                       }
                   }).fail(function () {
                       logger.logError('Error loading external authentication providers.', null, system.getModuleId(vm), true);
                       vm.loaded = false;
                   }).always(function () {
                       session.isBusy(false);
                       return dfd.resolve();
                   });

                return dfd.promise();
            }
            else {
                session.isBusy(false);
                return dfd.resolve();
            }
        }

        function goBack() {
            router.navigateBack();
        }

        function login() {
            if (vm.validationErrors().length > 0) {
                vm.validationErrors.showAllMessages();
                return;
            }

            session.isBusy(true);

            security.login({
                grant_type: "password",
                username: vm.userName(),
                password: vm.password()
            }).done(function (data) {
                if (data.userName && data.access_token) {
                    session.setUser(data, vm.rememberMe());
                    router.navigate('#/', 'replace');
                } else {
                    logger.logError('Error logging in.', null, system.getModuleId(vm), true);
                }
            }).always(function () {
                vm.userName('');
                vm.password('');
                session.isBusy(false);
            }).failJSON(function (data) {
                if (data && data.error_description) {
                    logger.logError('Error logging in.', data, system.getModuleId(vm), true);
                } else {
                    logger.logError('Error logging in.', null, system.getModuleId(vm), true);
                }
            });
        }

        function register() {
            router.navigate('#/register', 'replace');
        }
    });