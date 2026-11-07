using System.Reflection;
using AirlineCEO.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 程序化图标精灵的缓存语义（2026-11-07，报障「顾问气泡 / 航线图标 / 顶栏设置与倍速的符号没了，
    /// 位置只剩一块纯色实心方块」）。
    ///
    /// 根因不是贴图丢了，而是**缓存判定方式错了**：
    /// `UIFactory` 里这些图标是运行时 `Sprite.Create` 出来的，静态字段用 `??=` 缓存。
    /// 关闭域重载（`DisableDomainReload`）之后静态字段跨局存活，而退出 Play 时 Unity 会销毁
    /// 这些运行时对象 —— 销毁后的引用不是 C# null，`??=` 认为「已缓存」，于是把一块
    /// **已销毁的精灵**交给 `Image`，UGUI 画不出图形，就退化成实心色块。
    ///
    /// 与 PNG 图标（`GlobeSprite` / `CloudSprite`）的区别正在于此：那些是磁盘资源，
    /// 退出 Play 不会销毁，所以同一屏里「地球/云」正常、程序化图标全白 —— 这就是案发当时的画面。
    /// 这里用 `DestroyImmediate` 复刻「对象已销毁但静态引用还在」的同一条件，不需要真跑两局 Play。
    /// </summary>
    public class UIFactorySpriteCacheTests
    {
        static void AssertAlive(Sprite sprite, string label)
        {
            Assert.IsNotNull(sprite, label + " 返回了 null");
            Assert.IsFalse(sprite == null, label + " 返回的是已销毁对象（Unity 判空为真），Image 会画成纯色块");
            Assert.IsNotNull(sprite.texture, label + " 的贴图已丢失");
            Assert.Greater(sprite.rect.width, 0f, label + " 的矩形为空");
        }

        [Test]
        public void DestroyedCachedSprite_IsRebuilt()
        {
            var first = UIFactory.GearSprite;
            AssertAlive(first, "首次取齿轮图标");

            // 复刻退出 Play 时 Unity 对这些运行时对象的销毁（静态字段此刻仍指向它）
            Object.DestroyImmediate(first);

            var second = UIFactory.GearSprite;
            AssertAlive(second, "缓存被销毁后再取齿轮图标");
            Assert.AreNotSame(first, second, "已销毁的缓存必须重建，不能把旧引用再交出去");
        }

        [Test]
        public void DestroyedFastForwardSprite_IsRebuilt()
        {
            var first = UIFactory.FastForwardSprite(2);
            AssertAlive(first, "首次取 2× 快进图标");

            Object.DestroyImmediate(first);

            var second = UIFactory.FastForwardSprite(2);
            AssertAlive(second, "缓存被销毁后再取 2× 快进图标");
            Assert.AreNotSame(first, second, "指针数组里的槽位同样要按 Unity 判空重建");
        }

        [Test]
        public void AllProceduralIcons_SurviveDestroyAndRebuild()
        {
            // 顾问气泡 / 航线 / 设置 / 倍速 是用户报障那四个；其余同类图标一并钉住，避免下次换一个漏一个
            System.Func<Sprite>[] getters =
            {
                () => UIFactory.ChatSprite,
                () => UIFactory.RouteSprite,
                () => UIFactory.GearSprite,
                () => UIFactory.SearchSprite,
                () => UIFactory.ClockSprite,
                () => UIFactory.TailDownSprite,
                () => UIFactory.PauseSprite,
                () => UIFactory.HelpSprite,
                () => UIFactory.PlaySprite,
                () => UIFactory.FastForwardSprite(1),
                () => UIFactory.FastForwardSprite(2),
                () => UIFactory.FastForwardSprite(3),
            };

            foreach (var get in getters)
            {
                var sprite = get();
                AssertAlive(sprite, "程序化图标");
                Object.DestroyImmediate(sprite);
                AssertAlive(get(), "程序化图标（销毁后重建）");
            }
        }

        [Test]
        public void SessionReset_ClearsEveryCache()
        {
            // RuntimeInitializeOnLoadMethod 在 EditMode 测试里不会自己触发，直接调它验证效果：
            // 每局开始必须拿到全新精灵，而不是上一局残留的引用。
            var before = UIFactory.GearSprite;

            var reset = typeof(UIFactory).GetMethod("ResetSpriteCaches",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(reset, "UIFactory 缺少每局复位的 ResetSpriteCaches（关闭域重载后必需）");
            reset.Invoke(null, null);

            var after = UIFactory.GearSprite;
            AssertAlive(after, "复位后重新取齿轮图标");
            Assert.AreNotSame(before, after, "复位后必须重建，说明静态缓存确实被清空了");
        }

        /// <summary>
        /// Prefab 存不下运行时生成的圆角精灵（`m_Sprite` 存成 0，但 `m_Type: Sliced` 留着），
        /// 实例化后要按「Sliced 且无精灵」这个特征补回来，否则 Prefab 版面和代码现建的面板长得不一样。
        /// </summary>
        [Test]
        public void Rehydrate_FillsOnlySlicedImagesWithoutSprite()
        {
            var root = new GameObject("HydrateRoot", typeof(RectTransform));

            var missing = new GameObject("Missing", typeof(RectTransform), typeof(Image));
            missing.transform.SetParent(root.transform, false);
            var missingImg = missing.GetComponent<Image>();
            missingImg.type = Image.Type.Sliced;
            missingImg.sprite = null;

            var simple = new GameObject("Simple", typeof(RectTransform), typeof(Image));
            simple.transform.SetParent(root.transform, false);
            var simpleImg = simple.GetComponent<Image>();
            simpleImg.type = Image.Type.Simple;
            simpleImg.sprite = null;

            var other = new GameObject("Other", typeof(RectTransform), typeof(Image));
            other.transform.SetParent(root.transform, false);
            var otherImg = other.GetComponent<Image>();
            otherImg.type = Image.Type.Sliced;
            otherImg.sprite = UIFactory.CircleSprite;
            var keep = otherImg.sprite;

            UIFactory.RehydrateRoundedSprites(root);

            Assert.IsFalse(missingImg.sprite == null, "Sliced 且无精灵的 Image 必须补回圆角精灵");
            Assert.AreEqual(Image.Type.Sliced, missingImg.type, "补精灵不应改掉 Sliced（九宫格拉伸）");
            Assert.IsTrue(simpleImg.sprite == null, "Simple 的实色块不该被塞精灵");
            Assert.AreSame(keep, otherImg.sprite, "已有精灵的 Image 不该被覆盖");

            Object.DestroyImmediate(root);
        }
    }
}
