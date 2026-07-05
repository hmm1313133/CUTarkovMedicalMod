using System;
using System.Collections.Generic;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 统一针剂副作用管理器。
/// 各针剂的效果控制器通过 <see cref="Register"/> 注册自己的每秒副作用，
/// 本管理器挂在 Body 上统一执行扣除，避免每个针剂各自管理计时器。
///
/// 支持的副作用类型：
/// - HungerDrain：每秒扣饱食度（直接改 Body.hunger 字段，不走 Eat() 避免 pain/sickness 副作用）
/// - ThirstDrain：每秒扣水分（直接改 Body.thirst 字段，不走 Drink() 避免 eatTime/DropItem 副作用）
/// - LimbHealthDrain：每秒随机选一个非断肢、非要害部位扣 muscleHealth + skinHealth
///   多个针剂叠加时，每个针剂独立随机选部位，使损伤分散到不同部位
///
/// 设计要点：
/// - 饱食/水分直接改字段（反编译确认 hunger/thirst 是 public，游戏自身也直接 stfld）
/// - 部位健康扣除跳过 isHead/isVital/dismembered，避免直接致死
/// - 每个注册项独立追踪上次选中的部位索引，避免连续扣同一部位
/// </summary>
public sealed class StimSideEffectManager : MonoBehaviour
{
    private Body? _body;
    private readonly List<SideEffectEntry> _entries = new();
    private float _accumulator;

    /// <summary>
    /// 获取或创建 Body 上的副作用管理器实例。
    /// </summary>
    public static StimSideEffectManager GetOrCreate(Body body)
    {
        var manager = body.gameObject.GetComponent<StimSideEffectManager>();
        if (manager == null)
            manager = body.gameObject.AddComponent<StimSideEffectManager>();
        manager._body = body;
        return manager;
    }

    /// <summary>
    /// 注册一个针剂的每秒副作用。
    /// 返回一个 token，调用 <see cref="Unregister"/> 可移除。
    /// </summary>
    /// <param name="key">针剂唯一标识（如 "etg_c"）</param>
    /// <param name="hungerPerSecond">每秒饱食度扣除（正值=扣除，0=不扣）</param>
    /// <param name="thirstPerSecond">每秒水分扣除（正值=扣除，0=不扣）</param>
    /// <param name="limbHealthPerSecond">每秒随机部位健康扣除（正值=扣除，0=不扣）</param>
    public object Register(string key, float hungerPerSecond, float thirstPerSecond, float limbHealthPerSecond)
    {
        var entry = new SideEffectEntry
        {
            Key = key,
            HungerDrain = hungerPerSecond,
            ThirstDrain = thirstPerSecond,
            LimbHealthDrain = limbHealthPerSecond,
            LastLimbIndex = -1
        };
        _entries.Add(entry);
        enabled = true;
        Plugin.Log.LogInfo($"[SideEffect] Registered '{key}': hunger=-{hungerPerSecond}/s, thirst=-{thirstPerSecond}/s, limbHealth=-{limbHealthPerSecond}/s");
        return entry;
    }

    /// <summary>
    /// 注销一个针剂的副作用。
    /// </summary>
    public void Unregister(object token)
    {
        if (token is SideEffectEntry entry)
        {
            _entries.Remove(entry);
            Plugin.Log.LogInfo($"[SideEffect] Unregistered '{entry.Key}'");
            if (_entries.Count == 0)
                enabled = false;
        }
    }

    /// <summary>
    /// 立即执行一次性扣除（用于 Morphine 等即时副作用）。
    /// </summary>
    public static void ApplyInstant(Body body, float hungerDelta, float thirstDelta, float limbHealthDelta)
    {
        // 饱食/水分直接改字段（不走 Eat/Drink 避免副作用）
        if (hungerDelta != 0f)
            body.hunger = Mathf.Clamp(body.hunger - hungerDelta, -50f, 125f);
        if (thirstDelta != 0f)
            body.thirst = Mathf.Clamp(body.thirst - thirstDelta, -50f, 250f);

        // 部位健康随机扣一次
        if (limbHealthDelta > 0f && body.limbs != null)
        {
            var limb = PickRandomLimb(body, -1);
            if (limb != null)
            {
                limb.muscleHealth = Mathf.Max(0f, limb.muscleHealth - limbHealthDelta);
                limb.skinHealth = Mathf.Max(0f, limb.skinHealth - limbHealthDelta);
            }
        }
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null || _entries.Count == 0)
        {
            enabled = false;
            return;
        }

        _accumulator += Time.deltaTime;
        while (_accumulator >= 1f)
        {
            _accumulator -= 1f;
            TickAll();
        }
    }

    private void TickAll()
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (entry.HungerDrain > 0f)
                _body!.hunger = Mathf.Clamp(_body.hunger - entry.HungerDrain, -50f, 125f);
            if (entry.ThirstDrain > 0f)
                _body!.thirst = Mathf.Clamp(_body.thirst - entry.ThirstDrain, -50f, 250f);
            if (entry.LimbHealthDrain > 0f && _body!.limbs != null)
            {
                var limb = PickRandomLimb(_body, entry.LastLimbIndex);
                if (limb != null)
                {
                    entry.LastLimbIndex = System.Array.IndexOf(_body.limbs, limb);
                    limb.muscleHealth = Mathf.Max(0f, limb.muscleHealth - entry.LimbHealthDrain);
                    limb.skinHealth = Mathf.Max(0f, limb.skinHealth - entry.LimbHealthDrain);
                }
            }
        }
    }

    /// <summary>
    /// 随机选择一个非断肢、非要害、非头部的部位。
    /// 若有多个候选，尽量避免与上次相同。
    /// </summary>
    private static Limb? PickRandomLimb(Body body, int lastIndex)
    {
        var limbs = body.limbs;
        if (limbs == null || limbs.Length == 0) return null;

        var candidates = new List<int>();
        for (var i = 0; i < limbs.Length; i++)
        {
            var limb = limbs[i];
            if (limb == null || limb.dismembered) continue;
            if (limb.isVital || limb.isHead) continue;
            candidates.Add(i);
        }

        if (candidates.Count == 0) return null;

        int chosen;
        if (candidates.Count > 1 && lastIndex >= 0)
        {
            do
            {
                chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            } while (chosen == lastIndex);
        }
        else
        {
            chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        return limbs[chosen];
    }

    private sealed class SideEffectEntry
    {
        public string Key = "";
        public float HungerDrain;
        public float ThirstDrain;
        public float LimbHealthDrain;
        public int LastLimbIndex;
    }
}
