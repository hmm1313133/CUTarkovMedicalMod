using System;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 技能系统辅助工具：安全访问游戏原生 <see cref="Skills"/> 类的 STR/RES/INT 等级与经验系统。
///
/// 游戏使用三个属性（均为整数等级）：
///   STR = 力量 (Strength)        — 影响小游戏手部追踪力、俯卧撑速度、负重
///   RES = 耐力/韧性 (Resilience) — 影响小游戏手抖减少量、深蹲速度
///   INT = 智力 (Intelligence)    — 影响合成/交易等
///
/// 经验通过 <c>Skills.AddExp(int stat, float xp)</c> 添加，stat: 0=STR, 1=RES, 2=INT。
/// 经验会被全局倍率 <c>xpGainMult</c> 缩放，达到阈值后自动升级并播放升级提示。
/// </summary>
public static class SkillEffectHelper
{
    public const int StatSTR = 0; // 力量
    public const int StatRES = 1; // 耐力/韧性
    public const int StatINT = 2; // 智力

    /// <summary>
    /// 安全获取 Body 的 Skills 实例。
    /// </summary>
    public static Skills? GetSkills(Body? body)
    {
        if (body == null) return null;
        try { return body.skills; }
        catch { return null; }
    }

    /// <summary>
    /// 向指定属性添加经验（会受全局 xpGainMult 倍率影响，达阈值自动升级）。
    /// </summary>
    /// <param name="body">目标身体</param>
    /// <param name="stat">0=STR, 1=RES, 2=INT</param>
    /// <param name="xp">经验值</param>
    /// <returns>是否成功添加</returns>
    public static bool AddExp(Body? body, int stat, float xp)
    {
        var skills = GetSkills(body);
        if (skills == null || xp == 0f) return false;
        try
        {
            skills.AddExp(stat, xp);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SkillEffectHelper] AddExp(stat={stat}, xp={xp}) failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 临时调整整数属性等级（直接修改 STR/RES/INT 字段）。
    /// 用于针剂的临时增益/惩罚，结束后用相反数值恢复。
    /// </summary>
    public static bool AdjustLevel(Body? body, int stat, int delta)
    {
        var skills = GetSkills(body);
        if (skills == null || delta == 0) return false;
        try
        {
            switch (stat)
            {
                case StatSTR: skills.STR += delta; break;
                case StatRES: skills.RES += delta; break;
                case StatINT: skills.INT += delta; break;
                default: return false;
            }
            // 等级变化后更新经验边界，保证 UI 与升级检测一致
            skills.UpdateExpBoundaries();
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SkillEffectHelper] AdjustLevel(stat={stat}, delta={delta}) failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取当前属性等级。
    /// </summary>
    public static int GetLevel(Body? body, int stat)
    {
        var skills = GetSkills(body);
        if (skills == null) return 0;
        return stat switch
        {
            StatSTR => skills.STR,
            StatRES => skills.RES,
            StatINT => skills.INT,
            _ => 0
        };
    }

    /// <summary>
    /// 向身体注入兴奋剂震颤（miscShakeIntensity）。
    /// 这是游戏原生兴奋剂（Liquids.HighGradeStimulantStep 等）使用的同一字段，
    /// 在 HandleVisuals 中以 Mathf.Clamp01(miscShakeIntensity) * 0.05 叠加到视觉抖动。
    /// 值越大震颤越剧烈，每帧由 MoveTowards 衰减回 0。
    /// </summary>
    public static void AddStimulantTremor(Body? body, float intensity)
    {
        if (body == null || intensity <= 0f) return;
        try
        {
            body.miscShakeIntensity += intensity;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SkillEffectHelper] AddStimulantTremor(intensity={intensity}) failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 维持兴奋剂震颤强度（每帧调用，防止自然衰减过快消除震颤）。
    /// </summary>
    public static void MaintainStimulantTremor(Body? body, float targetIntensity)
    {
        if (body == null || targetIntensity <= 0f) return;
        try
        {
            if (body.miscShakeIntensity < targetIntensity)
                body.miscShakeIntensity = targetIntensity;
        }
        catch { }
    }

    // ===== 管视效应 (Tunnel Vision) =====
    // 使用 Screen Space - Overlay Canvas + 程序化径向渐变纹理实现暗角效果：
    // 视野中央透明，边缘逐渐变黑，模拟"透过管子看东西"的视野受限。
    // Image.alpha 随正弦波动在给定范围内周期性变化，模拟视觉不稳定。
    //
    // 实现：TunnelVisionOverlay 是一个 DontDestroyOnLoad 的 MonoBehaviour，
    // 使用 Screen Space - Overlay Canvas 渲染全屏 RawImage。
    // 初始化：Plugin.Awake 中调用 InitializeTunnelVision() 创建单例。

    /// <summary>
    /// 创建管视效果管理器（只需调用一次，在 Plugin.Awake 中）。
    /// </summary>
    public static void InitializeTunnelVision()
    {
        if (TunnelVisionOverlay.Instance != null) return;
        try
        {
            var go = new GameObject("TunnelVisionManager");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<TunnelVisionOverlay>();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SkillEffectHelper] InitializeTunnelVision failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 设置管视效应的波动范围。
    /// alpha = 1 - multiplier，乘数越小 → 暗角越强 → 管视越强。
    /// 乘数在 [min, max] 之间正弦波动（约3秒一个周期）。
    /// </summary>
    public static void SetTunnelVision(Body? body, float min, float max)
    {
        var overlay = TunnelVisionOverlay.Instance;
        if (overlay == null)
        {
            Plugin.Log.LogWarning("[SkillEffectHelper] SetTunnelVision: TunnelVisionOverlay.Instance is null! Overlay not initialized?");
            return;
        }
        overlay.Active = true;
        overlay.MinMultiplier = Mathf.Clamp01(min);
        overlay.MaxMultiplier = Mathf.Clamp01(max);
        Plugin.Log.LogInfo($"[SkillEffectHelper] SetTunnelVision: Active=true, min={overlay.MinMultiplier}, max={overlay.MaxMultiplier}");
    }

    /// <summary>
    /// 设置固定管视强度（min = max = multiplier 的快捷方式）。
    /// </summary>
    public static void SetTunnelVision(Body? body, float multiplier)
        => SetTunnelVision(body, multiplier, multiplier);

    /// <summary>
    /// 清除管视效应（遮罩渐隐并恢复正常视野）。
    /// </summary>
    public static void ClearTunnelVision(Body? body)
    {
        var overlay = TunnelVisionOverlay.Instance;
        if (overlay == null) return;
        overlay.Active = false;
        Plugin.Log.LogInfo("[SkillEffectHelper] ClearTunnelVision: Active=false.");
    }

    // ===== 瞄准镜视野扩展 (Scope Zoom) =====
    // 复用原版 autozoomgoggles 的 zoomTime 机制：
    // HandleCameraPosition 中 zoomTime > 0 时鼠标偏移倍率为 5x（扩展视野范围）。
    // 每帧由 HandleVariables Postfix 刷新 zoomTime 防止自然衰减。
    //
    // 与 autozoomgoggles 互斥：已装备护目镜时不叠加 AXMC 视野扩展。

    /// <summary>zoom 时的视野扩展时间（与 autozoomgoggles 一致）。</summary>
    private const float ScopeZoomTimeValue = 0.5f;

    private static bool _scopeZoomActive;

    /// <summary>
    /// 每帧检查并应用瞄准镜视野扩展。
    /// 条件：玩家有意识、主手持有 AXMC、未装备自动变焦护目镜。
    /// </summary>
    public static void UpdateScopeZoom(PlayerCamera? cam)
    {
        if (cam == null || cam.body == null) return;

        var body = cam.body;
        if (!body.conscious) return;

        try
        {
            // 已装备自动变焦护目镜时不叠加
            if (body.HasWearable("autozoomgoggles"))
            {
                if (_scopeZoomActive)
                {
                    _scopeZoomActive = false;
                    Plugin.Log.LogInfo("[SkillEffectHelper] Scope zoom deactivated (autozoomgoggles equipped).");
                }
                return;
            }

            var handItem = body.GetItem(body.handSlot);
            bool shouldZoom = handItem != null && handItem.id == AXMCItemSystem.ItemKey;

            if (shouldZoom)
            {
                cam.zoomTime = ScopeZoomTimeValue;
                if (!_scopeZoomActive)
                {
                    _scopeZoomActive = true;
                    Plugin.Log.LogInfo("[SkillEffectHelper] Scope zoom activated (AXMC).");
                }
            }
            else if (_scopeZoomActive)
            {
                _scopeZoomActive = false;
                Plugin.Log.LogInfo("[SkillEffectHelper] Scope zoom deactivated (AXMC unequipped).");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SkillEffectHelper] UpdateScopeZoom failed: {ex.Message}");
        }
    }
}
