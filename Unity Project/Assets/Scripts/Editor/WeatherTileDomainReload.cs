using AirlineCEO.Systems;
using AirlineCEO.UI;
using UnityEditor;

namespace AirlineCEO.Editor
{
    /// <summary>
    /// Domain Reload 前释放所有会跨越域存活的网络资源，避免
    /// 「Release of invalid GC handle. The handle is from a previous domain」。
    ///
    /// 成因（Editor.log 里的 native 栈）：
    ///   ScriptingGCHandle::ReleaseAndClear ← Coroutine::CleanupCoroutine
    ///     ← AsyncOperation::InvokeCoroutine ← UnityWebRequestAsyncOperation::InvokeCoroutine
    /// 即：「在协程里发的 UnityWebRequest 还在飞时发生了 Domain Reload」。
    /// 请求在旧域发出、在新域完成，Unity 随后清理旧域协程句柄失败并打印该消息。
    /// 请求还会一直挂到 timeout（日志里成片的 `Curl error 28: ... 15000 milliseconds`
    /// 就是瓦片与台风请求的 15s 超时），窗口被拉得很长，所以重编译几乎必中。
    ///
    /// 处置顺序：先中止在飞请求（AbortAll，此时旧域仍存活可用，请求当场收尾），
    /// 再停掉常驻循环，最后释放静态 HttpClient。
    /// </summary>
    [InitializeOnLoad]
    static class WeatherTileDomainReload
    {
        static WeatherTileDomainReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
        }

        static void OnBeforeReload()
        {
            WebRequestGuard.AbortAll();             // RealWeatherService / MapTileLayer / AIChatManager 的在飞请求
            RealWeatherService.ReleaseDomainResources();
            MapTileLayer.ReleaseDomainResources();
            MapWeatherTileLayer.ReleaseDomainResources();
        }
    }
}
