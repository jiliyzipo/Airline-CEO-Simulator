using AirlineCEO.UI.Navigation;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 弹窗返回登记簿的语义（2026-10-28，报障「提交开线研究后点底栏 &lt; 没反应」）。
    ///
    /// 这条链路的失败方式是**静默**的：栈空时 <c>TryDismissTop()</c> 返回 false，底栏 &lt; 就照旧
    /// 退页面 —— 看起来完全正常，只是弹窗一直挂在屏幕上。所以这里逐条钉住「谁被关、按什么顺序、
    /// 关完还留什么」，而不是只断一个 HasOpen。
    /// </summary>
    public class ModalBackStackTests
    {
        GameObject _a;
        GameObject _b;

        [SetUp]
        public void SetUp()
        {
            ModalBackStack.Clear();
            _a = new GameObject("ModalA");
            _b = new GameObject("ModalB");
        }

        [TearDown]
        public void TearDown()
        {
            ModalBackStack.Clear();
            if (_a != null) Object.DestroyImmediate(_a);
            if (_b != null) Object.DestroyImmediate(_b);
        }

        [Test]
        public void EmptyStack_LetsBackFallThrough()
        {
            Assert.IsFalse(ModalBackStack.HasOpen);
            Assert.IsFalse(ModalBackStack.TryDismissTop(),
                "栈空时必须返回 false，否则底栏 < 会吞掉本该退页面的返回");
        }

        [Test]
        public void Push_ThenBack_DismissesTopMostOnly()
        {
            int closedA = 0, closedB = 0;
            ModalBackStack.Push(_a, () => { closedA++; return true; });
            ModalBackStack.Push(_b, () => { closedB++; return true; });

            Assert.IsTrue(ModalBackStack.HasOpen);
            Assert.IsTrue(ModalBackStack.TryDismissTop());
            Assert.AreEqual(0, closedA, "后进先出：先关的是最后登记的那个");
            Assert.AreEqual(1, closedB);

            Assert.IsTrue(ModalBackStack.TryDismissTop());
            Assert.AreEqual(1, closedA);

            Assert.IsFalse(ModalBackStack.HasOpen);
            Assert.IsFalse(ModalBackStack.TryDismissTop());
        }

        [Test]
        public void Pop_UnregistersWithoutClosing()
        {
            int closed = 0;
            ModalBackStack.Push(_a, () => { closed++; return true; });
            ModalBackStack.Pop(_a);

            Assert.IsFalse(ModalBackStack.HasOpen);
            Assert.IsFalse(ModalBackStack.TryDismissTop());
            Assert.AreEqual(0, closed, "自行关闭的弹窗不应再被返回键关第二次");
        }

        [Test]
        public void ReRegisteringSameOwner_DoesNotStackDuplicates()
        {
            int closed = 0;
            for (int i = 0; i < 3; i++)
                ModalBackStack.Push(_a, () => { closed++; return true; });

            Assert.AreEqual(1, ModalBackStack.Count, "同一个弹窗重开三次不应在栈里堆三层");
            Assert.IsTrue(ModalBackStack.TryDismissTop());
            Assert.IsFalse(ModalBackStack.HasOpen);
            Assert.AreEqual(1, closed);
        }

        [Test]
        public void DestroyedOwner_IsPrunedInsteadOfBlockingBack()
        {
            ModalBackStack.Push(_b, () => true);
            Object.DestroyImmediate(_b);
            _b = null;

            Assert.IsFalse(ModalBackStack.HasOpen,
                "对象被销毁却留在登记簿里，会让底栏 < 一直处于可用态并消费掉本该退页面的返回");
        }
    }
}
