using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 多语言本地化服务。
/// 通过读取游戏原生 Locale.currentLangName 检测当前语言，
/// 从插件目录 Lang/{langCode}.json 加载翻译文件。
/// </summary>
public static class I18n
{
    private const string FallbackLanguage = "zh_CN";

    private static readonly Dictionary<string, string> _translations = new();
    private static string _lastDetectedLang = "";
    private static string _pluginDir = "";

    /// <summary>
    /// 当前实际加载的语言代码。
    /// </summary>
    public static string CurrentLanguage => _lastDetectedLang;

    /// <summary>
    /// 翻译键数量。
    /// </summary>
    public static int Count => _translations.Count;

    /// <summary>
    /// 翻译查找。找不到时返回 key 本身。
    /// </summary>
    public static string Tr(string key)
    {
        EnsureLoaded();
        return _translations.TryGetValue(key, out var value) ? value : key;
    }

    /// <summary>
    /// 带格式化参数的翻译查找。翻译文本中使用 {0}、{1} 占位符。
    /// </summary>
    public static string TrFmt(string key, params object[] args)
    {
        var template = Tr(key);
        if (args.Length == 0) return template;
        try
        {
            return string.Format(template, args);
        }
        catch
        {
            return template;
        }
    }

    /// <summary>
    /// 批量翻译多个键，返回数组。
    /// </summary>
    public static string[] TrAll(params string[] keys)
    {
        EnsureLoaded();
        var result = new string[keys.Length];
        for (int i = 0; i < keys.Length; i++)
            result[i] = _translations.TryGetValue(keys[i], out var v) ? v : keys[i];
        return result;
    }

    /// <summary>
    /// 强制重新加载翻译文件（语言切换后调用）。
    /// </summary>
    public static void Reload()
    {
        _lastDetectedLang = "";
        _translations.Clear();
        EnsureLoaded();
    }

    private static void EnsureLoaded()
    {
        var lang = DetectLanguage();
        if (lang == _lastDetectedLang && _translations.Count > 0) return;

        _lastDetectedLang = lang;
        _translations.Clear();

        var loaded = TryLoadLanguage(lang);
        if (!loaded && lang != FallbackLanguage)
        {
            Plugin.Log?.LogInfo($"[I18n] No translation file for '{lang}', falling back to '{FallbackLanguage}'.");
            loaded = TryLoadLanguage(FallbackLanguage);
            if (loaded) _lastDetectedLang = FallbackLanguage;
        }

        Plugin.Log?.LogInfo($"[I18n] Loaded {_translations.Count} translations for '{_lastDetectedLang}'.");
    }

    private static string DetectLanguage()
    {
        try
        {
            // 直接读取游戏原生 Locale.currentLangName（通过 Publicize 后可直接访问）
            var field = typeof(Locale).GetField("currentLangName",
                BindingFlags.Public | BindingFlags.Static);
            var name = field?.GetValue(null) as string;
            if (!string.IsNullOrEmpty(name)) return name!;
        }
        catch { }

        return FallbackLanguage;
    }

    private static bool TryLoadLanguage(string langCode)
    {
        var path = GetTranslationFilePath(langCode);
        if (!File.Exists(path)) return false;

        try
        {
            var json = File.ReadAllText(path);
            ParseFlatJson(json, _translations);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[I18n] Failed to load '{langCode}': {ex.Message}");
            return false;
        }
    }

    private static string GetTranslationFilePath(string langCode)
    {
        if (string.IsNullOrEmpty(_pluginDir))
        {
            _pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                         ?? BepInEx.Paths.PluginPath;
        }
        return Path.Combine(_pluginDir, "Lang", $"{langCode}.json");
    }

    /// <summary>
    /// 解析扁平 JSON（单层 key-value），支持嵌套路径展开为 dot-separated key。
    /// 例如 {"a": {"b": "value"}} -> key="a.b", value="value"
    /// </summary>
    private static void ParseFlatJson(string json, Dictionary<string, string> output)
    {
        // 简易 JSON 解析器：支持嵌套对象、字符串值、转义字符
        // 不依赖 Newtonsoft.Json / System.Text.Json（net48 兼容）
        var parser = new SimpleJsonParser(json);
        parser.ParseObject(output, "");
    }

    private sealed class SimpleJsonParser
    {
        private readonly string _json;
        private int _pos;

        internal SimpleJsonParser(string json)
        {
            _json = json;
            _pos = 0;
        }

        internal void ParseObject(Dictionary<string, string> output, string prefix)
        {
            SkipWhitespace();
            if (_pos >= _json.Length || _json[_pos] != '{') return;
            _pos++; // skip '{'

            while (true)
            {
                SkipWhitespace();
                if (_pos >= _json.Length) break;
                if (_json[_pos] == '}') { _pos++; break; }
                if (_json[_pos] == ',') { _pos++; continue; }

                // parse key
                var key = ParseString();
                if (key == null) break;

                SkipWhitespace();
                if (_pos >= _json.Length || _json[_pos] != ':') break;
                _pos++; // skip ':'

                SkipWhitespace();
                if (_pos >= _json.Length) break;

                var fullKey = string.IsNullOrEmpty(prefix) ? key : $"{prefix}.{key}";

                if (_json[_pos] == '{')
                {
                    // nested object
                    ParseObject(output, fullKey);
                }
                else if (_json[_pos] == '"')
                {
                    var value = ParseString();
                    if (value != null)
                        output[fullKey] = value;
                }
                else
                {
                    // skip non-string values (numbers, booleans, null, arrays)
                    SkipValue();
                }
            }
        }

        private string? ParseString()
        {
            if (_pos >= _json.Length || _json[_pos] != '"') return null;
            _pos++; // skip opening quote

            var sb = new System.Text.StringBuilder();
            while (_pos < _json.Length)
            {
                char c = _json[_pos++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && _pos < _json.Length)
                {
                    char esc = _json[_pos++];
                    switch (esc)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case '/': sb.Append('/'); break;
                        case 'u':
                            if (_pos + 4 <= _json.Length)
                            {
                                var hex = _json.Substring(_pos, 4);
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                                        System.Globalization.CultureInfo.InvariantCulture, out var code))
                                    sb.Append((char)code);
                                _pos += 4;
                            }
                            break;
                        default: sb.Append(esc); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private void SkipWhitespace()
        {
            while (_pos < _json.Length && char.IsWhiteSpace(_json[_pos]))
                _pos++;
        }

        private void SkipValue()
        {
            if (_pos >= _json.Length) return;
            char c = _json[_pos];
            if (c == '"') { ParseString(); }
            else if (c == '{') { SkipBraces('{', '}'); }
            else if (c == '[') { SkipBraces('[', ']'); }
            else
            {
                // skip until , or }
                while (_pos < _json.Length && _json[_pos] != ',' && _json[_pos] != '}')
                    _pos++;
            }
        }

        private void SkipBraces(char open, char close)
        {
            int depth = 0;
            while (_pos < _json.Length)
            {
                if (_json[_pos] == open) depth++;
                else if (_json[_pos] == close)
                {
                    depth--;
                    _pos++;
                    if (depth == 0) return;
                    continue;
                }
                else if (_json[_pos] == '"') { ParseString(); continue; }
                _pos++;
            }
        }
    }
}
