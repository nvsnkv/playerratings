﻿using System;
using System.Linq;
using System.Globalization;
using Microsoft.AspNet.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.OptionsModel;
using PlayerRatings.Localization;
using PlayerRatings.ViewModels.Home;
using PlayerRatings.Models;

namespace PlayerRatings.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IStringLocalizer<HomeController> _localizer;
        private readonly ILanguageData _languageData;
        private readonly IOptions<AppSettings> _settings;

        public HomeController(
            ApplicationDbContext context,
            IStringLocalizer<HomeController> localizer,
            ILanguageData languageData,
            IOptions<AppSettings> settings)
        {
            _context = context;
            _localizer = localizer;
            _languageData = languageData;
            _settings = settings;
        }

        public IActionResult Index()
        {
            var model = new IndexViewModel(_context.League.Count(), _context.Users.Count(), _context.Match.Count());

            return View(model);
        }

        public IActionResult About()
        {
            ViewData["Message"] = "Your application description page.";

            return View();
        }

        public IActionResult Contact()
        {
            ViewData["Message"] = "Your contact page.";

            return View();
        }

        public IActionResult Date()
        {
            return Content(DateTime.Now.ToString(CultureInfo.InvariantCulture));
        }

        public IActionResult Error()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ChangeLanguage(string language)
        {
            Response.Cookies.Append(_languageData.CookieName, language);
            return Redirect("/");
        }
    }
}
