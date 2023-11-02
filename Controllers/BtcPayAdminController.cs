﻿using Microsoft.AspNetCore.Mvc;
using Smartstore.ComponentModel;
using Smartstore.Core.Security;
using Smartstore.Engine.Modularity;
using Smartstore.Web.Controllers;
using Smartstore.Web.Modelling.Settings;
using Smartstore.BtcPay.Models;
using Smartstore.BtcPay.Settings;
using Smartstore.Core.Common.Services;
using Smartstore.Core;
using Autofac.Core;
using System.Web;
using Smartstore.Core.Stores;
using Newtonsoft.Json;
using static Smartstore.Core.Security.Permissions.Configuration;
using Microsoft.AspNetCore.Http;
using Org.BouncyCastle.Utilities.Collections;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using Smartstore.BtcPay.Services;

namespace Smartstore.BtcPay.Controllers
{
    [Area("Admin")]
    [Route("[area]/btcpay/{action=index}/{id?}")]
    public class BtcPayAdminController : ModuleController
    {

        private readonly ICommonServices _services;
        private readonly IProviderManager _providerManager;
        private readonly ICurrencyService _currencyService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public BtcPayAdminController(ICommonServices services,
                                     IHttpContextAccessor httpContextAccessor,
                                     IProviderManager providerManager,
                                     ICurrencyService currencyService)
        {
            _providerManager = providerManager;
            _httpContextAccessor = httpContextAccessor;
            _currencyService = currencyService;
            _services = services;
        }

        [LoadSetting, AuthorizeAdmin]
        public IActionResult Configure(BtcPaySettings settings)
        {
            var model = MiniMapper.Map<BtcPaySettings, ConfigurationModel>(settings);
            var myStore = _services.StoreContext.CurrentStore;

            ViewBag.Provider = _providerManager.GetProvider("Smartstore.BTCPay").Metadata;
            ViewBag.StoreCurrencyCode = _currencyService.PrimaryCurrency.CurrencyCode ?? "EUR";
            ViewBag.UrlWebHook = myStore.Url + "BtcPayHook/Process";

            var sViewMsg = HttpContext.Session.GetString("ViewMsg");
            if (!string.IsNullOrEmpty(sViewMsg))
            {
                ViewBag.ViewMsg = sViewMsg;
                HttpContext.Session.SetString("ViewMsg", "");
            }

            var sViewMsgError = HttpContext.Session.GetString("ViewMsgError");
            if (!string.IsNullOrEmpty(sViewMsgError))
            {
                ViewBag.ViewMsgError = sViewMsgError;
                HttpContext.Session.SetString("ViewMsgError", "");
            }

            var sUrl = "";
            if (!string.IsNullOrEmpty(model.BtcPayUrl))
            {
                
                sUrl = model.BtcPayUrl + (model.BtcPayUrl.EndsWith("/") ? "" : "/");
                sUrl += $"api-keys/authorize?applicationName={myStore.Name.Replace(" ", "")}&applicationIdentifier=SmartStore{myStore.Id}&selectiveStores=true"
                     + $"&redirect={myStore.Url}admin/btcpay/getautomaticapikeyconfig&permissions=btcpay.store.canmodifystoresettings";
            }
            ViewBag.UrlBtcApiKey = sUrl;
            ViewBag.UrlCreateWebHook = myStore.Url + "admin/btcpay/createwebhook/";
            return View(model);
        }

        [HttpPost, SaveSetting, AuthorizeAdmin]
        public IActionResult Configure(ConfigurationModel model, BtcPaySettings settings)
        {
            if (!ModelState.IsValid)
            {
                HttpContext.Session.SetString("ViewMsgError", "Incorrect data");
                return Configure(settings);
            }

            ModelState.Clear();
            MiniMapper.Map(model, settings);

            HttpContext.Session.SetString("ViewMsg", "Save OK");
            return RedirectToAction(nameof(Configure));
        }


        //[HttpPost, AuthorizeAdmin]
        [HttpPost]
        public async Task<IActionResult> GetAutomaticApiKeyConfig()
        {
           var settings = await _services.SettingFactory.LoadSettingsAsync<BtcPaySettings>(_services.StoreContext.CurrentStore.Id);
           try
            {
                string responseStr = await new StreamReader(Request.Body).ReadToEndAsync();
                var tblResponse = responseStr.Split('&');
                var sKey = (tblResponse.FirstOrDefault(a => a.StartsWith("apiKey")) ?? "").Split('=')[1];
                var sStoreID = (tblResponse.FirstOrDefault(a => a.StartsWith("permissions")) ?? "").Split('=')[1].Split("%3A")[1];

                settings.ApiKey = sKey;
                settings.BtcPayStoreID = sStoreID;

                HttpContext.Session.SetString("ViewMsg", "API Key and Store ID set with success. Don't forget to click on <b>Save</b> button to update data !");
            }
            catch (Exception ex)
            {
                HttpContext.Session.SetString("ViewMsgError", "Error during API Key creation !");
                Logger.Error(ex.Message);
            }
            return RedirectToAction(nameof(Configure), settings);

        }

        [HttpGet]
        public async Task<IActionResult> CreateWebHook()
        {
            var myStore = _services.StoreContext.CurrentStore;
            var settings = await _services.SettingFactory.LoadSettingsAsync<BtcPaySettings>(myStore.Id);

            if (! (string.IsNullOrEmpty(settings.BtcPayStoreID)
                   || string.IsNullOrEmpty(settings.BtcPayUrl)
                   || string.IsNullOrEmpty(settings.ApiKey)))
            {
                try
                {
                    BtcPayService apiService = new BtcPayService();
                    settings.WebHookSecret = apiService.CreateWebHook(settings, myStore.Url + "BtcPayHook/Process");
                    HttpContext.Session.SetString("ViewMsg", "WebHook created successfully. Don't forget to click on <b>Save</b> button to update data !");
                }
                catch (Exception ex)
                {
                    HttpContext.Session.SetString("ViewMsgError", "Error during WebHook creation! Make sure your API Key and Store ID are correct.");
                    Logger.Error(ex.Message);
              }
            }
            return RedirectToAction(nameof(Configure), settings);
        }

    }
}