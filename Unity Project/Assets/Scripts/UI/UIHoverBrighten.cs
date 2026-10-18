using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 按钮悬停时略微提亮底色（航空驾驶舱反馈）。**只动 RGB，不动 alpha**（见 OnPointerEnter 注）。
    /// 采用「进入时快照当前色 / 离开时原样还原」的无状态方案：
    /// 不再缓存构建期基准色，避免选中态等外部改色在鼠标离开后被旧基准色覆盖
    /// （旧实现会导致页签/底栏点击后的亮色被悬停还原逻辑吃掉）。
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class UIHoverBrighten : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Image _image;
        private Color _preHover;
        private bool _hovering;

        private void Awake()
        {
            _image = GetComponent<Image>();
        }

        private void OnEnable()
        {
            if (_image == null) _image = GetComponent<Image>();
            _hovering = false;
        }

        private void OnDisable()
        {
            // 悬停中途被隐藏/销毁：立即还原，避免提亮色残留到下次显示。
            if (_hovering && _image != null)
            {
                _image.color = _preHover;
                _hovering = false;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_image == null) return;
            _preHover = _image.color;   // 快照当前色（含选中亮色），而非构建期旧色
            _hovering = true;
            // 只提亮 RGB，**alpha 原样保留**。
            //
            // 本组件由 UIFactory.Button 无条件挂到**每一个**按钮上，其中一批按钮的底色是
            // 「透明但可点」的点击区（点名开档案、航线行、整屏「点外收起」接收层…）。
            // 旧写法直接 Lerp 到 Color.white（alpha = 1），会把它们的 a=0 拉到 0.22 ——
            // 光标一进这块区域，就凭空浮出一层 22% 灰雾盖住底下的文字
            //（2026-10-11 报：来源下拉之外的区域一移入光标就变灰）。
            //
            // 「提亮」的语义是把**已经画出来的**底色变亮，不是把没画的画出来。
            // 对不透明按钮（a=1）本行等价于旧行为；对半透明按钮（如 NavIdle a=0.92）
            // 也才是正确的「变亮」——旧写法顺带把它们的透明度也推向 1，那不是设计意图。
            Color lifted = Color.Lerp(_preHover, Color.white, 0.22f);
            lifted.a = _preHover.a;
            _image.color = lifted;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_image == null || !_hovering) return;
            _hovering = false;
            _image.color = _preHover;   // 精确还原悬停前的颜色，选中亮色不被吃掉
        }

        /// <summary>历史 API：悬停已改为进入快照/离开还原，不再缓存基准色，此方法保留为空实现。</summary>
        public void RefreshBase()
        {
        }
    }
}
