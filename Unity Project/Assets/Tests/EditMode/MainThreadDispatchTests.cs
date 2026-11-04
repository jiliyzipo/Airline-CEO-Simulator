using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 主线程派发与「取数管道只在线程池下载」的 NUnit 入口。断言在
    /// <see cref="MainThreadDispatchRegression"/> 里。
    ///
    /// 为什么这条必须在隔离跑批里（2026-11-04 独立复验 REV-20261104061859-ba39e0）：
    /// EditMode 133 项全绿、零跳过，Unity 却在测试跑完的 <c>Cleanup mono</c> 阶段崩掉，
    /// 进程返回 <c>1073741845</c>，整份交付因此判失败。崩溃栈是
    /// <c>ExternalDataFetcher</c> 的 <c>Task.Run</c> 续体里调 <c>JsonUtility.FromJson</c> ——
    /// 引擎 API 被放到线程池上，退出阶段拿到的是 NULL 的 MonoManager。
    /// 单测断言看不出这种问题（它发生在断言之后），只有「退出码必须为 0」的机器判据能抓住，
    /// 所以这里既锁派发语义，也锁源码结构。
    /// </summary>
    public class MainThreadDispatchTests
    {
        [Test]
        public void BackgroundWorkRunsOnMainThreadOnly()
        {
            Assert.IsTrue(MainThreadDispatchRegression.Run(),
                "主线程派发回归失败：看 Console 里「R49 主线程派发」那一行的 ✗ 项。"
                + "若「取数管道只在线程池下载=False」，说明某条 Task.Run 里又出现了 "
                + "JsonUtility / Application / Parse —— 那正是让 Unity 退出时崩溃的写法。");
        }
    }
}
