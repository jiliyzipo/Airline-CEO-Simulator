using AirlineCEO.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 键盘导航（GDB 29.5，P2 基础支持）：ESC 后退一层 / 关闭顶层弹窗。
    /// 输入框聚焦时跳过（交给输入框自行处理 Esc 取消输入）。
    /// </summary>
    public class KeyboardNav : MonoBehaviour
    {
        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;

            if (IsEditingText()) return;

            var router = NavigationRouter.Instance;
            if (router != null && router.StackDepth > 0)
            {
                router.Back();
            }
        }

        static bool IsEditingText()
        {
            var current = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (current == null) return false;
            return current.GetComponent<TMP_InputField>() != null ||
                   current.GetComponent<InputField>() != null;
        }
    }
}
