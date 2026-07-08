using System.IO;
using System.Reflection;
using BepInEx;
using UnityEngine;
using UnityEngine.Networking;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 注射器音效播放辅助类。
/// 所有注射器类 useAction 在开头调用 InjectorSound.Play() 播放 med_stimulator_use.wav 音效。
/// </summary>
public static class InjectorSound
{
    private static AudioClip? _cachedClip;
    private static bool _loaded;

    public static void Play()
    {
        if (!_loaded)
        {
            _loaded = true;

            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var path = Path.Combine(assemblyDir, "Framework", "Assets", "med_stimulator_use.wav");

            if (File.Exists(path))
            {
                _cachedClip = LoadWavSync(path);
                if (_cachedClip != null)
                    Plugin.Log.LogInfo("[InjectorSound] Audio clip loaded from file.");
                else
                    Plugin.Log.LogWarning("[InjectorSound] Failed to load audio clip from WAV file.");
            }
            else
            {
                // 回退：尝试从 Resources 加载
                _cachedClip = Resources.Load<AudioClip>("med_stimulator_use");
                if (_cachedClip != null)
                    Plugin.Log.LogInfo("[InjectorSound] Audio clip loaded from Resources.");
                else
                    Plugin.Log.LogWarning($"[InjectorSound] Audio file not found at: {path}");
            }
        }

        if (_cachedClip == null) return;

        var body = WorldGeneration.world?.body;
        if (body != null)
            AudioSource.PlayClipAtPoint(_cachedClip, body.transform.position);
    }

    private static AudioClip? LoadWavSync(string path)
    {
        try
        {
            using var uwr = UnityWebRequestMultimedia.GetAudioClip("file:///" + path, AudioType.WAV);
            uwr.SendWebRequest();
            while (!uwr.isDone) { }
            if (uwr.result == UnityWebRequest.Result.Success)
                return DownloadHandlerAudioClip.GetContent(uwr);
        }
        catch { }
        return null;
    }
}
