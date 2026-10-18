using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 跨 Domain Reload 的 UnityWebRequest 守卫。
    ///
    /// 为什么需要：`UnityWebRequest` 的 `yield return SendWebRequest()` 会登记一个协程
    /// （iterator 状态机）的 GC 句柄。若请求还在飞时发生 Domain Reload（改脚本触发重编译、
    /// 进出 Play 模式），请求会在**新域**里完成，Unity 随后清理**旧域**的协程句柄失败，打印：
    ///
    ///   Release of invalid GC handle. The handle is from a previous domain.
    ///   The release operation is skipped.
    ///
    /// native 栈可确认这条链路：
    ///   ScriptingGCHandle::ReleaseAndClear
    ///     ← Coroutine::CleanupCoroutine
    ///       ← AsyncOperation::InvokeCoroutine
    ///         ← UnityWebRequestAsyncOperation::InvokeCoroutine
    ///           ← BackgroundJobQueue::ExecuteMainThreadJobs
    ///
    /// 处置：在 `AssemblyReloadEvents.beforeAssemblyReload`（此时旧域仍存活可用）里
    /// 调 <see cref="AbortAll"/>，请求就在旧域内正常收尾，不再留悬空句柄。
    ///
    /// 注意：这条消息本身是 Unity 内部清理噪音，不代表玩法逻辑出错；但放任不管会掩盖真正的报错，
    /// 且每次重编译都刷一屏。凡是「在协程里发 UnityWebRequest」的地方都应当 Track/Untrack。
    /// </summary>
    public static class WebRequestGuard
    {
        static readonly List<UnityWebRequest> _live = new List<UnityWebRequest>(8);

        /// <summary>请求发出前登记。</summary>
        public static void Track(UnityWebRequest req)
        {
            if (req == null) return;
            if (!_live.Contains(req)) _live.Add(req);
        }

        /// <summary>请求收尾后注销（正常完成或失败都要调，否则列表只增不减）。</summary>
        public static void Untrack(UnityWebRequest req)
        {
            if (req == null) return;
            _live.Remove(req);
        }

        /// <summary>中止并释放所有仍在飞的请求。仅在 Domain Reload 前调用。</summary>
        public static void AbortAll()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var req = _live[i];
                if (req == null) continue;
                // Abort 让 AsyncOperation 立刻在当前域内完成；Dispose 释放 native 句柄。
                // 任一失败都不应阻断其余请求的清理。
                try { req.Abort(); } catch { /* 旧域句柄可能已失效 */ }
                try { req.Dispose(); } catch { /* 同上 */ }
            }
            _live.Clear();
        }

        /// <summary>
        /// 关闭 Domain Reload（Enter Play Mode Options）时静态量不会自动归零，
        /// 这里显式清一次，避免上一轮 Play 的残留请求被再次 Abort。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnSubsystemRegistration() => _live.Clear();
    }
}
