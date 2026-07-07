using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 管视遮罩效果：使用 URP Volume + Vignette 后处理实现暗角。
///
/// 原理：
///   创建全局 URP Volume，添加 Vignette 覆写。
///   通过增大 intensity 和降低 smoothness 来实现严重暗角。
///
/// 自愈设计：
///   - Volume 的 GameObject 在任何时候被 Unity 销毁后，
///     下一次 SetTunnelVision / Update 调用时自动重建。
///   - 不依赖 Instance 单例存活，而是用静态字段持有 Volume 引用。
///   - 即使场景切换销毁了 Volume，下一帧即刻重建。
/// </summary>
public sealed class TunnelVisionOverlay : MonoBehaviour
{
    /// <summary>单例实例</summary>
    public static TunnelVisionOverlay? Instance { get; private set; }

    /// <summary>管视是否激活</summary>
    public bool Active { get; set; }

    /// <summary>乘数波动范围最小值（越小管视越强，0=全黑）</summary>
    public float MinMultiplier { get; set; } = 0.0f;

    /// <summary>乘数波动范围最大值（越大管视越弱，1=完全透明）</summary>
    public float MaxMultiplier { get; set; } = 0.25f;

    // 静态持有：即使GameObject被销毁，Volume引用可能仍然有效
    private static Volume? _volume;
    private static Vignette? _vignette;

    private float _currentAlpha;

    private void Awake()
    {
        try
        {
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(gameObject);

            Instance = this;
            Plugin.Log.LogInfo("[TunnelVisionOverlay] Awake: Instance set.");

            CreateOrRecoverVolume();

            // 场景切换时自动关闭管视，防止残留到新游戏
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[TunnelVisionOverlay] Awake FAILED: {ex}");
        }
    }

    private void OnDestroy()
    {
        Plugin.Log.LogWarning($"[TunnelVisionOverlay] OnDestroy called! StackTrace: {Environment.StackTrace}");
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// 场景加载时强制关闭管视效果，防止跨存档残留。
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Plugin.Log.LogInfo($"[TunnelVisionOverlay] Scene loaded '{scene.name}', resetting tunnel vision.");
        Active = false;
        _currentAlpha = 0f;
        if (_vignette != null)
        {
            _vignette.intensity.value = 0f;
            _vignette.active = false;
        }
    }

    /// <summary>
    /// 创建 URP Volume + Vignette。如果已存在则复用以避免重复创建。
    /// </summary>
    private static void CreateOrRecoverVolume()
    {
        try
        {
            // 检查已有的 Volume 是否还活着
            if (_volume != null && _vignette != null)
            {
                Plugin.Log.LogInfo("[TunnelVisionOverlay] CreateOrRecoverVolume: Volume already exists, reusing.");
                return;
            }

            // 创建新的全局 Volume
            var go = new GameObject("TunnelVisionVolume");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);

            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 1000f; // 高优先级，确保覆盖其他 Volume

            // 获取或创建 profile
            var profile = _volume.profile;
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                _volume.profile = profile;
            }

            // 添加 Vignette 覆写
            if (!profile.TryGet(out _vignette))
            {
                _vignette = profile.Add<Vignette>(false);
            }

            if (_vignette == null)
            {
                Plugin.Log.LogError("[TunnelVisionOverlay] CreateOrRecoverVolume: Failed to create Vignette override!");
                return;
            }

            _vignette.active = true;
            _vignette.intensity.overrideState = true;
            _vignette.intensity.value = 0f;
            _vignette.smoothness.overrideState = true;
            _vignette.smoothness.value = 0.2f; // 锐利的过渡
            _vignette.color.overrideState = true;
            _vignette.color.value = Color.black;
            _vignette.rounded.overrideState = true;
            _vignette.rounded.value = false; // 不平滑圆角，更强硬的暗角

            Plugin.Log.LogInfo("[TunnelVisionOverlay] Created new global URP Volume with Vignette override.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[TunnelVisionOverlay] CreateOrRecoverVolume FAILED: {ex}");
            _volume = null;
            _vignette = null;
        }
    }

    /// <summary>
    /// 场景切换后调用，兼容旧接口。
    /// </summary>
    public void RefreshVolume()
    {
        Plugin.Log.LogInfo("[TunnelVisionOverlay] RefreshVolume: ensuring Volume exists...");
        CreateOrRecoverVolume();
    }

    private void Update()
    {
        // 自愈：如果 Volume 被销毁了，重建
        if (_volume == null || _vignette == null)
        {
            Plugin.Log.LogWarning("[TunnelVisionOverlay] Update: Volume/Vignette is null, recreating...");
            CreateOrRecoverVolume();
        }

        if (_vignette == null || _volume == null) return;

        // 确保 Vignette active 状态正确
        if (Active && !_vignette.active)
        {
            _vignette.active = true;
            Plugin.Log.LogInfo("[TunnelVisionOverlay] Update: reactivated Vignette.");
        }

        float targetAlpha;

        if (Active)
        {
            // 正弦波动周期约 3 秒
            var t = (Mathf.Sin(Time.time * 2f) + 1f) * 0.5f;
            var multiplier = Mathf.Lerp(MinMultiplier, MaxMultiplier, t);
            // multiplier 0.0 → alpha 1.0（完全漆黑）
            // multiplier 0.25 → alpha 0.75（仍然非常暗）
            targetAlpha = 1f - multiplier;
        }
        else
        {
            targetAlpha = 0f;
        }

        // 平滑过渡
        _currentAlpha = Mathf.Lerp(_currentAlpha, targetAlpha, Time.deltaTime * 12f);

        // 应用 Vignette intensity
        // URP Vignette intensity: 0=无效果, 1=最强暗角
        try
        {
            _vignette.intensity.value = _currentAlpha;
        }
        catch
        {
            // Vignette 引用可能已失效，下次 Update 会自愈
            _volume = null;
            _vignette = null;
        }
    }
}
