using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AirlineCEO.UI.Shell
{
    /// <summary>点 TMP &lt;link&gt; 时回调 link id。按字符命中，避免行距空隙落到视口后误点到旧链接。</summary>
    public class InboxLinkClick : MonoBehaviour, IPointerClickHandler
    {
        public TextMeshProUGUI text;
        public Action<string> onLink;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (text == null || !text.isActiveAndEnabled) return;
            text.ForceMeshUpdate();
            Camera cam = EventCamera(text);
            int index = TMP_TextUtilities.FindIntersectingLink(text, eventData.position, cam);
            if (index < 0)
                index = FindLinkByCharacter(text, eventData.position, cam);
            if (index < 0)
                index = FindLinkByPaddedGlyph(text, eventData.position, cam);
            if (index < 0) return;
            var info = text.textInfo.linkInfo[index];
            string id = info.GetLinkID();
            if (!string.IsNullOrEmpty(id))
                onLink?.Invoke(id);
        }

        static Camera EventCamera(TMP_Text text)
        {
            var canvas = text.canvas;
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;
            return canvas.worldCamera;
        }

        static int FindLinkByCharacter(TMP_Text text, Vector2 screen, Camera cam)
        {
            int ci = TMP_TextUtilities.FindIntersectingCharacter(text, screen, cam, true);
            if (ci < 0) return -1;
            var info = text.textInfo;
            for (int i = 0; i < info.linkCount; i++)
            {
                var link = info.linkInfo[i];
                int first = link.linkTextfirstCharacterIndex;
                int last = first + link.linkTextLength - 1;
                if (ci >= first && ci <= last)
                    return i;
            }
            return -1;
        }

        /// <summary>只在链接字形周围扩几像素，避免点到行距时落到别的词。</summary>
        static int FindLinkByPaddedGlyph(TMP_Text text, Vector2 screen, Camera cam)
        {
            const float pad = 10f;
            var info = text.textInfo;
            int found = -1;
            for (int i = 0; i < info.linkCount; i++)
            {
                var link = info.linkInfo[i];
                int last = link.linkTextfirstCharacterIndex + link.linkTextLength;
                for (int c = link.linkTextfirstCharacterIndex; c < last && c < info.characterCount; c++)
                {
                    var ch = info.characterInfo[c];
                    if (!ch.isVisible) continue;
                    Vector3 bl = text.transform.TransformPoint(ch.bottomLeft);
                    Vector3 tr = text.transform.TransformPoint(ch.topRight);
                    Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, bl);
                    Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, tr);
                    float minX = Mathf.Min(a.x, b.x) - pad;
                    float maxX = Mathf.Max(a.x, b.x) + pad;
                    float minY = Mathf.Min(a.y, b.y) - pad;
                    float maxY = Mathf.Max(a.y, b.y) + pad;
                    if (screen.x >= minX && screen.x <= maxX && screen.y >= minY && screen.y <= maxY)
                    {
                        if (found >= 0 && found != i) return -1;
                        found = i;
                        break;
                    }
                }
            }
            return found;
        }
    }
}
