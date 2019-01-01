using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using LiteDB;
using Microsoft.Scripting.Utils;
using Playnite.API;
using Playnite.SDK;
using Playnite.SDK.Metadata;
using Playnite.SDK.Models;
using VndbSharp;
using VndbSharp.Models;
using VndbSharp.Models.VisualNovel;

namespace Playnite.Metadata.Providers
{
    public class VNDBMetadataProvider
    {
        private Vndb _vndbClient;

        public GameMetadata GetMetadata(String id)
        {
            if (_vndbClient == null)
                _vndbClient = new Vndb();

            var vnId = UInt32.Parse(id);

            var vn = this._vndbClient.GetVisualNovelAsync(VndbFilters.Id.Equals(vnId), 
                    VndbFlags.FullVisualNovel)
                    /* Please Kill Me :( */.Result
                    .First();
            var producers = this._vndbClient.GetReleaseAsync(VndbFilters.VisualNovel.Equals(vnId), VndbFlags.Producers)
                /* Please Kill Me :( */.Result
                .SelectMany(r => r.Producers)
                .ToArray();

            var game = new Game(vn.Name);
            game.CoverImage = vn.Image;
            // TODO: Add image into database, to reduce stress on vndb cdn?
            // Prefer NSFW Screenshots
            game.BackgroundImage = vn.Screenshots.FirstOrDefault(s => !s.IsNsfw)?.Url
                                   ?? vn.Screenshots.FirstOrDefault()?.Url;
            game.Description = this.ConvertBBCode(vn.Description);

            if (vn.Released.Year.HasValue && vn.Released.Month.HasValue && vn.Released.Day.HasValue)
                game.ReleaseDate = new DateTime((Int32)vn.Released.Year.Value, vn.Released.Month.Value, vn.Released.Day.Value);

            game.Categories = new ComparableList<String>(new [] { "Visual Novel" });
//          game.Tags = new ComparableList<String>(vn.Tags.Select(t => t.Id.ToString())); // TODO: Map this to the TagDump

            if (game.Links == null)
                game.Links = new ObservableCollection<Link>();

            if (!string.IsNullOrWhiteSpace(vn.VisualNovelLinks.Wikipedia))
                game.Links.Add(new Link("Wikipedia", "https://en.wikipedia.org/wiki/" + vn.VisualNovelLinks.Wikipedia));
            if (!string.IsNullOrWhiteSpace(vn.VisualNovelLinks.Renai))
                game.Links.Add(new Link("Renai", "https://renai.us/game/" + vn.VisualNovelLinks.Renai));

            game.Developers =
                new ComparableList<String>(producers.Where(p => p.IsDeveloper).Select(p => p.Name).Distinct());
            game.Publishers =
                new ComparableList<String>(producers.Where(p => p.IsPublisher).Select(p => p.Name).Distinct());

            var metadata = new GameMetadata();
            metadata.Image = new MetadataFile(vn.Image);
            metadata.BackgroundImage = game.BackgroundImage;
            metadata.GameData = game;
            

            return metadata;
        }

        public (String id, String name, String description)[] SearchMetadata(String query)
        {
            if (_vndbClient == null)
                _vndbClient = new Vndb();

            var searchResults = new List<VisualNovel>();
            var hasMore = true;

            while (hasMore)
            {
                var pageResults = this._vndbClient.GetVisualNovelAsync(VndbFilters.Search.Fuzzy(query),
                    VndbFlags.Basic | VndbFlags.Details)
                    /* Please Kill Me :( */.Result;

                searchResults.AddRange(pageResults.Items);
                hasMore = pageResults.HasMore;
            }

            return searchResults.Select(r =>
                (r.Id.ToString(), r.Name,
                    (r.Description.IndexOf("\n") > 0
                        ? r.Description.Substring(0, r.Description.IndexOf("\n")) + " ..."
                        : r.Description))).ToArray();
        }

        private String ConvertBBCode(String bbcode)
        {
            if (String.IsNullOrWhiteSpace(bbcode))
                return bbcode;
            
            // Only handles urls.
            return Regex.Replace(bbcode, "\\[url=([^\\s\\]]+)\\s*\\](.*?(?=\\[\\/url\\]))\\[\\/url\\]", m =>
            {
                var link = m.Groups[1].Value;
                if (link[0] == '/')
                    link = $"https://vndb.org/{link}";
                return $"<a href=\"{link}\">{m.Groups[2].Value}</a>";
            })
            // And new lines!
            .Replace("\n", "<br/>\n");
        }

    }
}
