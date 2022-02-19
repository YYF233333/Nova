﻿using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nova
{
    public class I18nFontSize : MonoBehaviour
    {
        public List<LocaleFloatPair> multipliers;

        private Text text;
        private TMP_Text textPro;
        private TextProxy textProxy;

        private float initFontSize;

        private float fontSize
        {
            get
            {
                if (textProxy != null)
                {
                    textProxy.Init();
                    return textProxy.fontSize;
                }
                else if (textPro != null)
                {
                    return textPro.fontSize;
                }
                else
                {
                    return text.fontSize;
                }
            }
            set
            {
                if (textProxy != null)
                {
                    textProxy.fontSize = value;
                }
                else if (textPro != null)
                {
                    textPro.fontSize = value;
                }
                else
                {
                    text.fontSize = Mathf.RoundToInt(value);
                }
            }
        }

        private void Awake()
        {
            text = GetComponent<Text>();
            textPro = GetComponent<TMP_Text>();
            textProxy = GetComponent<TextProxy>();
            this.RuntimeAssert(text != null || textPro != null || textProxy != null,
                "Missing Text or TMP_Text or TextProxy.");

            initFontSize = fontSize;
        }

        private void UpdateFontSize()
        {
            foreach (var pair in multipliers)
            {
                if (pair.locale == I18n.CurrentLocale)
                {
                    fontSize = initFontSize * pair.value;
                    return;
                }
            }

            fontSize = initFontSize;
        }

        private void OnEnable()
        {
            UpdateFontSize();
            I18n.LocaleChanged.AddListener(UpdateFontSize);
        }

        private void OnDisable()
        {
            I18n.LocaleChanged.RemoveListener(UpdateFontSize);
        }
    }
}