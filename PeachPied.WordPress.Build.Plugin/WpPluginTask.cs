﻿using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace PeachPied.WordPress.Build.Plugin
{
    /// <summary>
    /// Task parses wp plugin/theme metadata and sets corresponsing properties.
    /// </summary>
    public class WpPluginTask : Task
    {
        /// <summary>
        /// Directory with the project, expecting to contain the plugin metadat files.
        /// </summary>
        [Required]
        public string ProjectPath { get; set; }

        /// <summary>
        /// WP slug id. Used to resolve eventual icon URL.
        /// </summary>
        [Required]
        public string WpSlug { get; set; }

        /// <summary>
        /// The wp-content sub-dir.
        /// </summary>
        [Required]
        public string WpContentTarget { get; set; }

        /// <summary>
        /// Specified VersionSuffix if any.
        /// </summary>
        public string VersionSuffix { get; set; }

        /// <summary>
        /// Version from metadata.
        /// </summary>
        [Output]
        public string Version { get; set; }

        /// <summary>
        /// Project URL from metadata.
        /// </summary>
        [Output]
        public string PackageProjectUrl { get; set; }

        /// <summary>
        /// Tags from metadata.
        /// </summary>
        [Output]
        public string PackageTags { get; set; }

        /// <summary>
        /// Authors from metadata.
        /// </summary>
        [Output]
        public string Authors { get; set; }

        /// <summary>
        /// Title from metadata.
        /// </summary>
        [Output]
        public string Title { get; set; }

        /// <summary>
        /// Description from metadata.
        /// </summary>
        [Output]
        public string Description { get; set; }

        /// <summary>
        /// Optional icon URL from metadata.
        /// </summary>
        [Output]
        public string PackageIconUrl { get; set; }

        //[Output]
        //public List<ITaskItem> MarketplaceAssets { get; } = new List<ITaskItem>();

        bool IsPlugin => WpContentTarget.EndsWith("plugins", StringComparison.Ordinal); // plugins, mu-plugins

        bool IsTheme => WpContentTarget == "themes";

        /// <summary>Matches wp metadata in a .php file; Tag:Value</summary>
        static readonly Regex s_regex_meta = new Regex(@"^[ \t\/*#@]*(?<Tag>[a-zA-Z ]+):[ \t]*(?<Value>.+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// Collects metadata.
        /// </summary>
        public override bool Execute()
        {

            var meta = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            var sections = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

            if (IsPlugin)
            {
                // meta
                foreach (var fname in Directory.GetFiles(ProjectPath, "*.php")) // php files in the directory
                {
                    foreach (var line in File.ReadLines(fname).Take(26)) // up to 26 lines
                    {
                        var m = s_regex_meta.Match(line);
                        if (m.Success)
                        {
                            meta[m.Groups["Tag"].Value.Trim()] = m.Groups["Value"].Value.Trim();
                            Log.LogMessage(m.Value, MessageImportance.High);
                        }
                    }

                    if (meta.ContainsKey("version") && meta.ContainsKey("plugin name")) break;
                }
            }

            // sections
            foreach (var f in new[] { "metadata.txt", "readme.txt" })
            {
                var fname = Path.Combine(ProjectPath, f);
                if (File.Exists(fname))
                {
                    string section = null;
                    foreach (var line in File.ReadLines(fname))
                    {
                        Match m;
                        if ((m = Regex.Match(line, @"^\s*==+\s+(?<Name>[^=]+)==+\s*")).Success)
                        {
                            if (!meta.ContainsKey("title"))
                            {
                                meta["title"] = m.Groups["Name"].Value.Trim();
                            }
                            else
                            {
                                section = m.Groups["Name"].Value.Trim();
                                sections[section] = "";
                            }
                        }
                        else if (section == null)
                        {
                            if ((m = Regex.Match(line, @"^(?<Tag>[a-zA-Z ]+):[ \t]*(?<Value>.+)$")).Success)
                            {
                                meta[m.Groups["Tag"].Value.Trim()] = m.Groups["Value"].Value.Trim();
                                Log.LogMessage(m.Value, MessageImportance.High);
                                continue;
                            }
                            else if (!string.IsNullOrWhiteSpace(line))
                            {
                                // short description follows tags
                                Description += (Description != null ? Environment.NewLine : null) + line;
                            }
                        }
                        else if (section != null)
                        {
                            sections[section] += line + "\n";
                        }
                    }
                }
            }

            if (IsPlugin)
            {
                // icon
                foreach (var urlformat in new[] { "https://ps.w.org/{0}/assets/icon-256x256.png", "https://ps.w.org/{0}/assets/icon-256x256.jpg", "https://ps.w.org/{0}/assets/icon.svg", "https://ps.w.org/{0}/assets/icon-128x128.png" })
                {
                    var url = string.Format(urlformat, WpSlug);
                    if (CheckUrl(url))
                    {
                        PackageIconUrl = url;
                        break;
                    }
                }
            }

            if (IsTheme)
            {
                // screenshot
                var screenshot = Directory.GetFiles(ProjectPath, "screenshot.*").FirstOrDefault();
                if (screenshot != null)
                {
                    //PackageIconUrl = FeedUrl + "assets/wptheme/" + WpSlug + Path.GetExtension(screenshot);
                }
            }

            //// readme
            //var dict = Directory.GetFiles(ProjectPath)  // [.extension, readmepath ]
            //    .Where(x => Path.GetFileNameWithoutExtension(x).Equals("readme", StringComparison.OrdinalIgnoreCase))
            //    .ToDictionary(Path.GetExtension, StringComparer.OrdinalIgnoreCase);

            //string readme;
            //if (dict.TryGetValue(".txt", out readme) || dict.TryGetValue(".md", out readme)) // read .txt if possible
            //{
            //    //var remotepath = "assets/" + MarketplaceTarget + "/" + Slug + Path.GetExtension(readme).ToLower();
            //    //Upload(readme, remotepath);
            //    MarketplaceAssets.Add(new TaskItem(readme));
            //}

            // properties
            string s;
            Version = ConstructVersion((meta.TryGetValue("version", out s) || meta.TryGetValue("Stable tag", out s)) ? s : null, VersionSuffix);
            PackageProjectUrl = (meta.TryGetValue("Plugin URI", out s) || meta.TryGetValue("website", out s) || meta.TryGetValue("theme uri", out s) || meta.TryGetValue("url", out s)) ? s : null;
            PackageTags = (meta.TryGetValue("tags", out s) || sections.TryGetValue("tags", out s)) ? s.Replace(", ", ",") : null;
            Authors = (meta.TryGetValue("Author", out s) || meta.TryGetValue("contributors", out s)) ? s : null;
            Title = string.IsNullOrWhiteSpace(Title) ? (meta.TryGetValue("Plugin Name", out s) || meta.TryGetValue("title", out s)) ? s : null : Title;

            // short description
            if (meta.TryGetValue("Description", out s))
            {
                // short description in metadata
                Description = s;
            }
            else if (string.IsNullOrWhiteSpace(Description) && sections.TryGetValue("Description", out s))
            {
                // short description not read from header,
                // get it from full description section
                var emptyline = new Regex(@"^\s+$", RegexOptions.CultureInvariant | RegexOptions.Multiline);

                // normalize whitespaces,
                // take first paragraph
                s = s.Replace("\r\n", "\n");
                s = emptyline.Replace(s, "");

                var nl = s.IndexOf("\n\n", StringComparison.Ordinal);
                if (nl > 0)
                    s = s.Remove(nl);

                //
                Description = s;
            }

            // done
            return true;
        }

        static string ConstructVersion(string metaversion, string versionsuffix)
        {
            if (string.IsNullOrEmpty(metaversion))
            {
                return null;
            }

            // vX.Y.Z-PreRelease
            if (metaversion[0] == 'v') metaversion = metaversion.Substring(1);

            var dash = metaversion.IndexOf('-');
            var prefix = dash < 0 ? metaversion : metaversion.Remove(dash);
            var suffix = string.IsNullOrEmpty(versionsuffix)
                ? (dash < 0 ? "" : metaversion.Substring(dash + 1))
                : versionsuffix;

            return string.IsNullOrEmpty(suffix) ? prefix : $"{prefix}-{suffix}";
        }

        static bool CheckUrl(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "HEAD";
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (200 == (int)response.StatusCode)
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }


    }
}
