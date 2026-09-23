using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SolidWorksTeamRenameTool
{
    internal sealed class UserSettings
    {
        public V2RuleConfig Config { get; set; }
        public string ActiveTemplate { get; set; }
        public V2RuleTarget ActiveRule { get; set; }
        public bool ParamsExpanded { get; set; }
        public bool AutoConfirmDialogs { get; set; }
        public bool ConfirmOverwrite { get; set; }

        public UserSettings()
        {
            Config = V2RuleConfig.CreateDefault();
            ActiveTemplate = "prefix";
            ActiveRule = V2RuleTarget.Assembly;
            ConfirmOverwrite = true;
        }
    }

    internal static class UserSettingsStore
    {
        private const int CurrentVersion = 1;

        public static bool TryLoad(out UserSettings settings, out string error)
        {
            settings = null;
            error = null;

            string path = SettingsPath();
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                XDocument doc = XDocument.Load(path);
                XElement root = doc.Root;
                if (root == null || root.Name != "Settings")
                {
                    throw new InvalidDataException("Invalid settings root.");
                }

                UserSettings loaded = new UserSettings();
                loaded.ActiveTemplate = Attr(root, "ActiveTemplate", "prefix");
                loaded.ActiveRule = ParseRuleTarget(Attr(root, "ActiveRule", "Assembly"));
                loaded.ParamsExpanded = ParseBool(Attr(root, "ParamsExpanded", "false"));
                loaded.AutoConfirmDialogs = ParseBool(Attr(root, "AutoConfirmDialogs", "false"));
                loaded.ConfirmOverwrite = ParseBool(Attr(root, "ConfirmOverwrite", "true"));

                XElement configElement = root.Element("Config");
                if (configElement != null)
                {
                    loaded.Config = ReadConfig(configElement);
                }

                V2RuleConfig.Normalize(loaded.Config);
                settings = loaded;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                settings = null;
                return false;
            }
        }

        public static void Save(UserSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            string path = SettingsPath();
            string folder = Path.GetDirectoryName(path);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            V2RuleConfig config = settings.Config == null ? V2RuleConfig.CreateDefault() : settings.Config.Clone();
            V2RuleConfig.Normalize(config);

            XDocument doc = new XDocument(
                new XElement("Settings",
                    new XAttribute("Version", CurrentVersion),
                    new XAttribute("ActiveTemplate", settings.ActiveTemplate ?? "prefix"),
                    new XAttribute("ActiveRule", settings.ActiveRule),
                    new XAttribute("ParamsExpanded", settings.ParamsExpanded),
                    new XAttribute("AutoConfirmDialogs", settings.AutoConfirmDialogs),
                    new XAttribute("ConfirmOverwrite", settings.ConfirmOverwrite),
                    WriteConfig(config)));

            doc.Save(path);
        }

        private static V2RuleConfig ReadConfig(XElement element)
        {
            V2RuleConfig config = new V2RuleConfig();
            config.Mode = ParseRenameMode(Attr(element, "Mode", "Rule"));
            config.FindText = Attr(element, "FindText", string.Empty);
            config.ReplaceText = Attr(element, "ReplaceText", string.Empty);
            config.ProjectCode = Attr(element, "ProjectCode", "Y01");

            XElement filters = element.Element("Filters");
            if (filters != null)
            {
                config.Filters.SkipEnglishStart = ParseBool(Attr(filters, "SkipEnglishStart", "false"));
                config.Filters.SkipChineseStart = ParseBool(Attr(filters, "SkipChineseStart", "false"));
                config.Filters.SkipNumberStart = ParseBool(Attr(filters, "SkipNumberStart", "false"));
                config.Filters.SkipSymbolStart = ParseBool(Attr(filters, "SkipSymbolStart", "false"));
                config.Filters.SkipDuplicateReferences = ParseBool(Attr(filters, "SkipDuplicateReferences", "true"));
                config.Filters.SkipReadonlyFiles = ParseBool(Attr(filters, "SkipReadonlyFiles", "true"));
                config.Filters.SkipPathKeywords = ParseBool(Attr(filters, "SkipPathKeywords", "true"));
                config.Filters.PathKeywords.Clear();
                config.Filters.PathKeywords.AddRange(filters.Elements("Keyword").Select(k => (k.Value ?? string.Empty).Trim()).Where(k => k.Length > 0));
            }

            config.AssemblyFields.Clear();
            config.PartFields.Clear();

            XElement assembly = element.Element("AssemblyFields");
            if (assembly != null)
            {
                foreach (XElement field in assembly.Elements("Field"))
                {
                    config.AssemblyFields.Add(ReadField(field));
                }
            }

            XElement part = element.Element("PartFields");
            if (part != null)
            {
                foreach (XElement field in part.Elements("Field"))
                {
                    config.PartFields.Add(ReadField(field));
                }
            }

            return config;
        }

        private static XElement WriteConfig(V2RuleConfig config)
        {
            return new XElement("Config",
                new XAttribute("Mode", config.Mode),
                new XAttribute("FindText", config.FindText ?? string.Empty),
                new XAttribute("ReplaceText", config.ReplaceText ?? string.Empty),
                new XAttribute("ProjectCode", config.ProjectCode ?? string.Empty),
                new XElement("Filters",
                    new XAttribute("SkipEnglishStart", config.Filters.SkipEnglishStart),
                    new XAttribute("SkipChineseStart", config.Filters.SkipChineseStart),
                    new XAttribute("SkipNumberStart", config.Filters.SkipNumberStart),
                    new XAttribute("SkipSymbolStart", config.Filters.SkipSymbolStart),
                    new XAttribute("SkipDuplicateReferences", config.Filters.SkipDuplicateReferences),
                    new XAttribute("SkipReadonlyFiles", config.Filters.SkipReadonlyFiles),
                    new XAttribute("SkipPathKeywords", config.Filters.SkipPathKeywords),
                    config.Filters.PathKeywords.Select(k => new XElement("Keyword", k ?? string.Empty))),
                new XElement("AssemblyFields", config.AssemblyFields.Select(WriteField)),
                new XElement("PartFields", config.PartFields.Select(WriteField)));
        }

        private static V2RuleField ReadField(XElement element)
        {
            V2FieldType type = ParseFieldType(Attr(element, "Type", string.Empty));
            return V2RuleConfig.Field(
                ParseInt(Attr(element, "Id", "0"), 0),
                V2RuleConfig.FieldTypeLabel(type),
                Attr(element, "Joiner", string.Empty),
                type,
                Attr(element, "Param1", V2RuleConfig.DefaultParam1(type)),
                Attr(element, "Param2", V2RuleConfig.DefaultParam2(type)));
        }

        private static XElement WriteField(V2RuleField field)
        {
            return new XElement("Field",
                new XAttribute("Id", field.Id),
                new XAttribute("Type", field.Type),
                new XAttribute("Joiner", field.Joiner ?? string.Empty),
                new XAttribute("Param1", field.Param1 ?? string.Empty),
                new XAttribute("Param2", field.Param2 ?? string.Empty));
        }

        private static string SettingsPath()
        {
            return Path.Combine(PortableRoot(), "settings.xml");
        }

        private static string PortableRoot()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                DirectoryInfo directory = new DirectoryInfo(baseDirectory);
                if (directory.Name.Equals("Release", StringComparison.OrdinalIgnoreCase) &&
                    directory.Parent != null &&
                    directory.Parent.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) &&
                    directory.Parent.Parent != null)
                {
                    return directory.Parent.Parent.FullName;
                }

                return directory.FullName;
            }
            catch
            {
                return baseDirectory;
            }
        }

        private static string Attr(XElement element, string name, string fallback)
        {
            XAttribute attribute = element == null ? null : element.Attribute(name);
            return attribute == null ? fallback : attribute.Value;
        }

        private static bool ParseBool(string text)
        {
            bool value;
            return bool.TryParse(text, out value) && value;
        }

        private static int ParseInt(string text, int fallback)
        {
            int value;
            return int.TryParse(text, out value) ? value : fallback;
        }

        private static V2RenameMode ParseRenameMode(string text)
        {
            try
            {
                return (V2RenameMode)Enum.Parse(typeof(V2RenameMode), text, true);
            }
            catch
            {
                throw new InvalidDataException("Unknown rename mode: " + text);
            }
        }

        private static V2RuleTarget ParseRuleTarget(string text)
        {
            try
            {
                return (V2RuleTarget)Enum.Parse(typeof(V2RuleTarget), text, true);
            }
            catch
            {
                return V2RuleTarget.Assembly;
            }
        }

        private static V2FieldType ParseFieldType(string text)
        {
            try
            {
                return (V2FieldType)Enum.Parse(typeof(V2FieldType), text, true);
            }
            catch
            {
                throw new InvalidDataException("Unknown field type: " + text);
            }
        }
    }
}
