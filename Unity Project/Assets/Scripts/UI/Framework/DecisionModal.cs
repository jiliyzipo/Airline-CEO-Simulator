using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AirlineCEO.UI.Framework
{
    /// <summary>决策模态：遮罩 + 主/次按钮。</summary>
    public class DecisionModal : BasePanel
    {
        [SerializeField] Button primaryButton;
        [SerializeField] Button secondaryButton;
        [SerializeField] TextMeshProUGUI bodyLabel;
        [SerializeField] Image dimmer;

        public event Action Confirmed;
        public event Action Cancelled;

        public void AssignModalRefs(Button primary, Button secondary, TextMeshProUGUI body, Image dimmerImg)
        {
            primaryButton = primary;
            secondaryButton = secondary;
            bodyLabel = body;
            dimmer = dimmerImg;
        }

        protected override void Awake()
        {
            base.Awake();
            if (primaryButton != null)
                primaryButton.onClick.AddListener(() => { Confirmed?.Invoke(); Hide(); });
            if (secondaryButton != null)
                secondaryButton.onClick.AddListener(() => { Cancelled?.Invoke(); Hide(); });
        }

        public void SetBody(string text)
        {
            if (bodyLabel != null) bodyLabel.text = text;
        }

        public void SetPrimaryLabel(string text)
        {
            if (primaryButton == null) return;
            var tmp = primaryButton.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) tmp.text = text;
        }
    }
}
