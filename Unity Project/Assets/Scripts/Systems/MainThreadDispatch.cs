using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 主线程收件箱：后台线程只负责下载，把要执行的动作投递进来，由主线程排空后执行。
    ///
    /// 为什么必须有这一层（2026-11-04 独立复验实测崩溃）：
    /// <c>ExternalDataFetcher.RequestRefresh</c> 等取数管道用 <c>Task.Run</c> 在**线程池**上下载，
    /// 下载完就地调 <c>JsonUtility.FromJson</c> 解析。而 <c>JsonUtility</c> 是引擎 API，只能在主线程调用：
    /// 线程池线程拿到的是 NULL 的 MonoManager。平时它表现为"偶尔解析失败"，到编辑器**退出阶段**
    /// （<c>Cleanup mono</c> 之后）就升格为致命错误：
    ///
    ///   Cleanup mono
    ///   GetManagerFromContext: pointer to object of manager 'MonoManager' is NULL (table index 5)
    ///   Fatal Error! … Crash!!!
    ///
    /// 进程因此以 <c>1073741845</c>（0xC0000005 类访问违例）退出 —— 测试全绿、退出码却非 0，
    /// 独立检测只能判失败（REV-20261104061859-ba39e0）。修法不是"退出时少做点事"，
    /// 而是**根本不在后台线程碰 Unity API**：这里的 <see cref="Post"/> 是后台线程唯一的出口。
    ///
    /// 约定：
    /// <list type="bullet">
    /// <item>后台线程只调 <see cref="Post"/>，不直接调 <c>Parse</c> / <c>JsonUtility</c> / <c>Application.*</c>；</item>
    /// <item>主线程在既有节拍里调 <see cref="Drain"/>（游戏时钟、界面轮询、编辑器 update）；</item>
    /// <item>退出中投递的委托一律丢弃 —— 那时已经没有主线程会来执行它们。</item>
    /// </list>
    /// </summary>
    public static class MainThreadDispatch
    {
        static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        static int _mainThreadId;
        static volatile bool _quitting;
        static CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>当前是否在主线程（未捕获到主线程时返回 <c>false</c>，调用方应保守处理）。</summary>
        public static bool IsMainThread
            => _mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        /// <summary>进程/编辑器是否正在退出。退出后不得再执行任何引擎 API。</summary>
        public static bool IsQuitting => _quitting;

        /// <summary>待排空件数（诊断用，不要求精确）。</summary>
        public static int PendingCount => Queue.Count;

        /// <summary>进入 Play 时复位（关闭 Domain Reload 时静态量不会自动归零）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Boot()
        {
            CaptureMainThread();
            _quitting = false;
            _cts = new CancellationTokenSource();
            Action dropped;
            while (Queue.TryDequeue(out dropped)) { }
            Application.quitting -= MarkQuitting;
            Application.quitting += MarkQuitting;
        }

        /// <summary>记录主线程 id。必须在主线程调用（Play 启动钩子与编辑器加载钩子都会调）。</summary>
        internal static void CaptureMainThread()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        /// 标记「正在退出」。退出钩子（<c>Application.quitting</c> / <c>EditorApplication.quitting</c>）调它；
        /// 隔离回归也用它验证「退出中丢弃」这条语义。公开是为了跨程序集（Editor 回归在另一个 asmdef）。
        /// 同时取消 <see cref="ShutdownToken"/>：把还在飞的 HTTP 请求掐掉，
        /// 免得退出时线程池上还挂着 socket 回调（那类残留同样会把退出码弄脏）。
        /// </summary>
        public static void MarkQuitting()
        {
            _quitting = true;
            var cts = _cts;
            if (cts == null) return;
            try { cts.Cancel(); } catch { /* 已释放 / 已取消都无害 */ }
        }

        /// <summary>
        /// 把退出标记复位。**只给隔离回归用**：它要验证「退出中丢弃」这条语义，
        /// 而退出标记是进程级静态量，测完必须复位，否则同一编辑器里后续取数再也不会排空。
        /// 生产路径没有调用方 —— 退出是不可逆的。
        /// </summary>
        public static void ResetQuittingForTests()
        {
            _quitting = false;
            _cts = new CancellationTokenSource();
        }

        /// <summary>退出令牌。后台下载把它交给 <c>HttpClient</c>，退出时请求立刻中止。</summary>
        public static CancellationToken ShutdownToken
        {
            get
            {
                var cts = _cts;
                return cts != null ? cts.Token : CancellationToken.None;
            }
        }

        /// <summary>后台线程投递一件必须在主线程执行的工作。退出中直接丢弃。</summary>
        public static void Post(Action work)
        {
            if (work == null) return;
            if (_quitting) return;
            Queue.Enqueue(work);
        }

        /// <summary>
        /// 主线程排空。返回执行了几件。
        /// 单件抛异常只记一行警告并继续 —— 后台取数失败不该连累其它管道。
        /// </summary>
        public static int Drain()
        {
            if (_quitting)
            {
                // 退出中：把积压丢掉。留着只会延长域的生命周期，且执行时引擎已拆到一半。
                Action ignored;
                while (Queue.TryDequeue(out ignored)) { }
                return 0;
            }

            int done = 0;
            Action work;
            while (Queue.TryDequeue(out work))
            {
                done++;
                try { work(); }
                catch (Exception e) { Debug.LogWarning($"[主线程派发] 一件工作失败：{e.Message}"); }
            }
            return done;
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// 编辑器侧的排空泵。EditMode 自测与批处理跑测试时**没有**游戏时钟，
    /// 只靠 <see cref="MainThreadDispatch.Drain"/> 的调用点排空会把结果一直压在队列里，
    /// 于是"取数没反应"，而积压的委托在退出时仍会尝试触碰引擎。这里每秒跟着编辑器泵一次。
    /// </summary>
    [UnityEditor.InitializeOnLoad]
    static class MainThreadDispatchEditorPump
    {
        static MainThreadDispatchEditorPump()
        {
            MainThreadDispatch.CaptureMainThread();
            UnityEditor.EditorApplication.update -= Pump;
            UnityEditor.EditorApplication.update += Pump;
            UnityEditor.EditorApplication.quitting -= MainThreadDispatch.MarkQuitting;
            UnityEditor.EditorApplication.quitting += MainThreadDispatch.MarkQuitting;
        }

        static void Pump() => MainThreadDispatch.Drain();
    }
#endif
}
