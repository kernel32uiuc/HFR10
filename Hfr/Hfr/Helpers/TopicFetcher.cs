﻿using Hfr.Model;
using Hfr.Utilities;
using Hfr.ViewModel;
using Hfr.Views.MainPages;
using HtmlAgilityPack;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;

namespace Hfr.Helpers
{
    public static class TopicFetcher
    {
        public static async Task GetPosts(Topic currentTopic)
        {
            Debug.WriteLine("Fetching Posts");
            await Fetch(currentTopic);
            Debug.WriteLine("Updating UI with new Posts list");
        }

        public static async Task Fetch(Topic currentTopic)
        {
            await ThreadUI.Invoke(() =>
            {
                Loc.Topic.IsTopicLoading = true;
            });

            var html = await HttpClientHelper.Get(currentTopic.TopicDrapURI);
            if (string.IsNullOrEmpty(html)) return;

            await ThreadUI.Invoke(() =>
            {
                Loc.Topic.CurrentTopic.Html = html;
            });

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            var topicText = htmlDoc.DocumentNode.Descendants("div")
                    .Where(x => x.GetAttributeValue("id", "").Contains("para"))
                    .
                    Select(y => y.InnerHtml).ToArray();

            var messCase1 = htmlDoc.DocumentNode.Descendants("td").Where(x =>
                        x.GetAttributeValue("class", "").Contains("messCase1") &&
                        x.InnerHtml.Contains("<div><b class=\"s2\">Publicité</b></div>") == false &&
                        x.InnerHtml.Contains("Auteur") == false)
                        .Select(x => x.InnerHtml).ToArray();

            string[] toolbar = htmlDoc.DocumentNode.Descendants("div")
                        .Where(x => (string)x.GetAttributeValue("class", "") == "toolbar")
                        .Select(x => x.InnerHtml).ToArray();

            int i = 0;
            string TempHTMLMessagesList = "";
            string TempHTMLMessage = "";
            string TempHTMLTopic = "";

            string BodyTemplate = "";
            string MessageTemplate = "";

            // This is absolutely quick and dirty code :o
            Assembly asm = typeof(App).GetTypeInfo().Assembly;

            using (Stream stream = asm.GetManifestResourceStream(HFRRessources.Tpl_Topic))
            using (StreamReader reader = new StreamReader(stream))
            {
                BodyTemplate = reader.ReadToEnd();
            }

            using (Stream stream = asm.GetManifestResourceStream(HFRRessources.Tpl_Message))
            using (StreamReader reader = new StreamReader(stream))
            {
                MessageTemplate = reader.ReadToEnd();
            }

            foreach (var post in topicText)
            {
                TempHTMLMessage = MessageTemplate;

                // Pseudo
                int firstPseudo = messCase1[i].IndexOf("<b class=\"s2\">", StringComparison.Ordinal) +
                                  "<b class=\"s2\">".Length;
                int lastPseudo = messCase1[i].LastIndexOf("</b>", StringComparison.Ordinal);
                var Pseudo = messCase1[i].Substring(firstPseudo, lastPseudo - firstPseudo);
                Pseudo = Pseudo.Replace(((char)8203).ToString(), ""); // char seperator (jocebug)

                // Content
                int lastPostText = topicText[i].IndexOf("<div style=\"clear: both;\"> </div>", StringComparison.Ordinal);
                if (lastPostText == -1)
                {
                    lastPostText = topicText[i].Length;
                }
                var Content = topicText[i].Substring(0, lastPostText);
                Content = Regex.Replace(WebUtility.HtmlDecode(Content), " target=\"_blank\"", "");

                // Date et heure
                int firstDate = toolbar[i].IndexOf("Posté le ") + "Posté le ".Length; ;
                int lastDate = 31;
                var dateHeure = Regex.Replace(toolbar[i].Substring(firstDate, lastDate), "&nbsp;", " ");

                // Id de la réponse
                int firstReponseId = messCase1[i].IndexOf("title=\"n°") + "title=\"n°".Length;
                int lastlastReponseId = messCase1[i].LastIndexOf("\" alt=\"n°");
                var reponseId = messCase1[i].Substring(firstReponseId, lastlastReponseId - firstReponseId);

                // Affichage des avatars
                var avatarUri = "";
                var avatarClass = "";

                if (messCase1[i].Contains("avatar_center"))
                {
                    int firstAvatar = messCase1[i].IndexOf("<div class=\"avatar_center\" style=\"clear:both\"><img src=\"") + "<div class=\"avatar_center\" style=\"clear:both\"><img src=\"".Length;
                    int lastAvatar = messCase1[i].LastIndexOf("\" alt=\"");
                    avatarUri = messCase1[i].Substring(firstAvatar, lastAvatar - firstAvatar);
                }
                else
                {
                    avatarUri = "ms-appx-web:///Assets/HTML/UI/rsz_no_avatar.png";
                    avatarClass = "no_avatar";
                }

                TempHTMLMessage = TempHTMLMessage.Replace("%%ID%%", i.ToString());
                TempHTMLMessage = TempHTMLMessage.Replace("%%POSTID%%", reponseId);

                TempHTMLMessage = TempHTMLMessage.Replace("%%no_avatar_class%%", avatarClass);
                TempHTMLMessage = TempHTMLMessage.Replace("%%AUTEUR_AVATAR%%", avatarUri);
                TempHTMLMessage = TempHTMLMessage.Replace("%%AUTEUR_PSEUDO%%", Pseudo);
                TempHTMLMessage = TempHTMLMessage.Replace("%%MESSAGE_DATE%%", dateHeure);

                TempHTMLMessage = TempHTMLMessage.Replace("%%MESSAGE_CONTENT%%", Content);

                TempHTMLMessagesList += TempHTMLMessage;
                i++;
            }

            // Get URL of the new post form
            var url = htmlDoc.DocumentNode.Descendants("form").FirstOrDefault(x=>x.GetAttributeValue("id", "") == "repondre_form").GetAttributeValue("action", "");
            currentTopic.TopicNewPostUriForm = WebUtility.HtmlDecode(url);

            TempHTMLTopic = BodyTemplate.Replace("%%MESSAGES%%", TempHTMLMessagesList);

            // Create/Open WebSite-Cache folder
            var subfolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(Strings.WebSiteCacheFolderName, CreationCollisionOption.OpenIfExists);
            var file = await subfolder.CreateFileAsync($"{Strings.WebSiteCacheFileName}", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, TempHTMLTopic);

            await ThreadUI.Invoke(() =>
            {
                Loc.Topic.UpdateTopicWebView(currentTopic);

                Loc.Topic.IsTopicLoading = false;
            });
        }
    }
}
