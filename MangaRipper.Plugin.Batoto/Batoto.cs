﻿using MangaRipper.Core.Interfaces;
using MangaRipper.Core.Models;
using MangaRipper.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MangaRipper.Plugin.Batoto
{
    /// <summary>
    /// Support find chapters, images from Batoto
    /// </summary>
    public class Batoto : IMangaService
    {
        private ILogger Logger;
        private readonly Downloader downloader;
        private readonly IXPathSelector selector;
        private string _username = "gufrohepra";
        private string _password = "123";
        private List<string> selectedLanguages = new List<string>();

        public Batoto(IConfiguration config, ILogger myLogger, Downloader downloader, IXPathSelector selector)
        {
            Logger = myLogger;
            this.downloader = downloader;
            this.selector = selector;
            if (config == null)
            {
                return;
            }
            Configuration(config.FindConfigByPrefix("Plugin.Batoto"));
        }

        private void Configuration(IEnumerable<KeyValuePair<string, object>> settings)
        {
            // TODO FIX THIS
            var settingCollection = settings.ToArray();
            if (settingCollection.Any(i => i.Key.Equals("Plugin.Batoto.Username")))
            {
                var user = settingCollection.First(i => i.Key.Equals("Plugin.Batoto.Username")).Value;
                Logger.Info($@"Current Username: {_username}. New Username: {user}");
                _username = user as string;
            }

            if (settingCollection.Any(i => i.Key.Equals("Plugin.Batoto.Password")))
            {
                var pass = settingCollection.First(i => i.Key.Equals("Plugin.Batoto.Password")).Value;
                Logger.Info($@"Current Password: {_password}. New Password: {pass}");
                _password = pass as string;
            }

            if (settingCollection.Any(i => i.Key.Equals("Plugin.Batoto.Languages")))
            {
                var languages = settingCollection.First(i => i.Key.Equals("Plugin.Batoto.Languages")).Value as string;
                Logger.Info($@"Only the follow languages will be selected: {languages}");
                selectedLanguages = languages.Split(new char[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).ToList();
            }
        }

        public SiteInformation GetInformation()
        {
            return new SiteInformation(nameof(Batoto), "http://bato.to", "Multiple Languages");
        }

        public bool Of(string link)
        {
            var uri = new Uri(link);
            return uri.Host.Equals("bato.to");
        }

        public async Task<IEnumerable<Chapter>> FindChapters(string manga, IProgress<int> progress, CancellationToken cancellationToken)
        {
            progress.Report(0);
            downloader.Cookies = LoginBatoto(_username, _password);
            downloader.Referrer = "http://bato.to/reader";

            // find all chapters in a manga
            string input = await downloader.DownloadStringAsync(manga, cancellationToken);
            var title = selector.Select(input, "//h1").InnerHtml.Trim();
            var langs = selector.SelectMany(input, "//tr[contains(@class,'chapter_row')]//div").Select(n => n.Attributes["title"]).ToList();
            var chaps = selector.SelectMany(input, "//tr[contains(@class,'chapter_row')]//a[@title]")
                .Select(n =>
                    {
                        string originalName = n.Attributes["title"];
                        originalName = originalName.Remove(originalName.LastIndexOf('|')).Trim();
                        return new Chapter(originalName, n.Attributes["href"]) { Manga = title };
                    }).ToList();
            for (int i = 0; i < langs.Count(); i++)
            {
                chaps[i].Language = langs[i];
            }
            chaps = chaps.GroupBy(c => c.Url).Select(g => g.First()).ToList();
            if (selectedLanguages.Count > 0)
            {
                chaps = chaps.Where(c => selectedLanguages.Contains(c.Language)).ToList();
            }
            progress.Report(100);
            return chaps;
        }

        public async Task<IEnumerable<string>> FindImages(Chapter chapter, IProgress<int> progress, CancellationToken cancellationToken)
        {
            progress.Report(0);
            downloader.Cookies = LoginBatoto(_username, _password);
            downloader.Referrer = "http://bato.to/reader";

            // find all pages in a chapter
            var chapterUrl = TransformChapterUrl(chapter.Url);
            var input = await downloader.DownloadStringAsync(chapterUrl, cancellationToken);
            var pages = selector.SelectMany(input, "//select[@name='page_select']/option").Select(n => n.Attributes["value"]);
            // transform pages link
            var transformedPages = pages.Select(TransformChapterUrl).ToList();

            // find all images in pages
            int current = 0;
            var images = new List<string>();
            foreach (var page in transformedPages)
            {
                var pageHtml = await downloader.DownloadStringAsync(page, cancellationToken);
                var image = selector
                .Select(pageHtml, "//img[@id='comic_page']").Attributes["src"];

                images.Add(image);
                var f = (float)++current / transformedPages.Count();
                int i = Convert.ToInt32(f * 100);
                progress.Report(i);
            }
            progress.Report(100);
            return images.Distinct();
        }

        private CookieCollection LoginBatoto(string user, string password)
        {
            var request =
               WebRequest.CreateHttp("https://bato.to/forums/index.php?app=core&module=global&section=login&do=process");
            request.Method = WebRequestMethods.Http.Post;
            var postData =
                $@"auth_key=880ea6a14ea49e853634fbdc5015a024&referer=https%3A%2F%2Fbato.to%2F&ips_username={user}&ips_password={password}&rememberMe=1";

            byte[] byteArray = Encoding.UTF8.GetBytes(postData);
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = byteArray.Length;
            request.CookieContainer = new CookieContainer();
            using (Stream dataStream = request.GetRequestStream())
            {
                dataStream.Write(byteArray, 0, byteArray.Length);
            }

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                Logger.Debug("Login response from Batoto");
                return response.Cookies;
            }
        }

        private string TransformChapterUrl(string url)
        {
            var id = url.Substring(url.LastIndexOf('#') + 1);
            var page = "1";
            if (id.LastIndexOf('_') != -1)
            {
                page = id.Substring(id.LastIndexOf('_') + 1);
                id = id.Remove(id.LastIndexOf('_'));
            };
            return $@"https://bato.to/areader?id={id}&p={page}";
        }
    }
}
