#nullable enable
using System;
using ForbiddenEden.Core;
using ForbiddenEden.Net;
using ForbiddenEden.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ForbiddenEden.Screens
{
    /// <summary>Base for every screen. Screens are built once from code and then shown or hidden.</summary>
    public abstract class GameScreen : MonoBehaviour
    {
        public GameApp App = null!;
        public RectTransform Root = null!;

        protected EdenApiClient Api => App.Api;
        protected GameSession Session => App.Session;
        protected GameSettings Settings => App.Settings;

        /// <summary>A full-screen overlay hides the screens under it while it is open.</summary>
        public virtual bool HidesBelow => false;

        public abstract void Build();
        public virtual void OnShow() { }
        public virtual void OnHide() { }
        public virtual void OnOverlaysClosed() { }

        /// <summary>Return true when the screen handled Esc itself.</summary>
        public virtual bool HandleEscape() => false;

        // ---- shared chrome -----------------------------------------------------------

        /// <summary>Dark glass behind a menu screen so text stays readable over the 3D scene.</summary>
        protected Image Backdrop(float alpha = 0.55f)
        {
            Image img = UIKit.Block(Root, "Backdrop", new Color(0.02f, 0.04f, 0.09f, alpha), true);
            img.rectTransform.Fill();
            return img;
        }

        /// <summary>Breadcrumb + large title at the top-left, as on every wireframe.</summary>
        protected void Heading(string crumb, string title, string? note = null)
        {
            RectTransform head = UIKit.Rect(Root, "Heading").PlaceTL(0.03f, 0.035f, 0.70f, 0.11f);
            Text c = UIKit.Label(head, crumb, 20, Theme.Accent);
            c.rectTransform.Place(0, 0.62f, 1, 1);
            Text t = UIKit.Label(head, title, 44, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            t.rectTransform.Place(0, 0, 1, 0.68f);
            // Headings sit over bright 3D sets; a soft shadow keeps them readable.
            foreach (Text label in new[] { c, t })
            {
                var s = label.gameObject.AddComponent<Shadow>();
                s.effectColor = new Color(0, 0, 0, 0.75f);
                s.effectDistance = new Vector2(2, -2);
            }
            if (note != null)
            {
                Text n = UIKit.Label(Root, note, 18, Theme.TextMuted, TextAnchor.UpperRight);
                n.rectTransform.PlaceTL(0.55f, 0.06f, 0.42f, 0.05f);
            }
        }

        /// <summary>The HS2-style 返回 button in the bottom-left corner.</summary>
        protected Button BackButton(UnityAction onClick, string label = "‹ 返回")
        {
            Button b = UIKit.Button(Root, label, onClick, ButtonStyle.Menu, 24);
            b.ButtonText().alignment = TextAnchor.MiddleCenter;
            ((RectTransform)b.transform).PlaceTL(0.025f, 0.905f, 0.10f, 0.06f);
            return b;
        }

        /// <summary>Pop for overlays, or a custom action.</summary>
        protected Button CloseButton(UnityAction? onClick = null)
        {
            Button b = UIKit.Button(Root, "✕ 關閉", onClick ?? (() => App.Pop()), ButtonStyle.Ghost, 22);
            ((RectTransform)b.transform).PlaceTL(0.875f, 0.04f, 0.10f, 0.055f);
            return b;
        }

        /// <summary>The three round 音／字／語 buttons from the web version, top-right.</summary>
        protected RectTransform SystemButtons(float top = 0.03f)
        {
            RectTransform row = UIKit.Rect(Root, "SystemButtons").PlaceTL(0.855f, top, 0.13f, 0.055f);
            UIKit.HBox(row, 10, 0, TextAnchor.MiddleRight, true);
            RoundButton(row, AudioListener.volume > 0.01f ? "音" : "靜", b =>
            {
                AudioListener.volume = AudioListener.volume > 0.01f ? 0f : 1f;
                b.ButtonText().text = AudioListener.volume > 0.01f ? "音" : "靜";
                App.Toast(AudioListener.volume > 0.01f ? "已開啟聲音" : "已靜音");
            });
            RoundButton(row, "字", _ => FontMenu());
            RoundButton(row, "語", _ => LanguageMenu());
            return row;
        }

        private void RoundButton(RectTransform row, string label, Action<Button> onClick)
        {
            Button b = null!;
            b = UIKit.Button(row, label, () => onClick(b), ButtonStyle.Menu, 22);
            b.GetComponent<Image>().sprite = UIKit.Circle;
            b.GetComponent<Image>().type = Image.Type.Simple;
            b.ButtonText().alignment = TextAnchor.MiddleCenter;
            b.ButtonText().rectTransform.Inset(0, 0, 0, 0);
            b.Pref(56, 56);
        }

        protected void FontMenu()
        {
            float[] scales = { 0.9f, 1f, 1.15f };
            string[] names = { "小", "中", "大" };
            int i = Array.IndexOf(scales, Settings.fontScale);
            int next = (i + 1) % scales.Length;
            App.Confirm("字級", "目前字級：" + names[Mathf.Max(0, i)] + "\n切換成「" + names[next] + "」？畫面會重新排版。", "切換", () =>
            {
                Settings.fontScale = scales[next];
                Settings.Save();
                UIKit.FontScale = Settings.fontScale;
                App.RebuildAll();
            });
        }

        protected void LanguageMenu() => App.Push<LanguageScreen>();

        /// <summary>A titled glass panel, the basic box of the wireframes.</summary>
        protected Image Box(RectTransform parent, string title, float left, float top, float width, float height, out RectTransform body, int pad = 18)
        {
            Image panel = UIKit.Panel(parent, "Box:" + title, Theme.Panel);
            panel.rectTransform.PlaceTL(left, top, width, height);
            if (title.Length > 0)
            {
                Text t = UIKit.Label(panel.transform, title, 22, Theme.Gold, TextAnchor.UpperLeft, FontStyle.Bold);
                t.rectTransform.Place(0, 1, 1, 1);
                t.rectTransform.offsetMin = new Vector2(pad, -48);
                t.rectTransform.offsetMax = new Vector2(-pad, -12);
                body = UIKit.Rect(panel.transform, "Body").Fill().Inset(pad, 52, pad, pad);
            }
            else
            {
                body = UIKit.Rect(panel.transform, "Body").Fill().Inset(pad, pad, pad, pad);
            }
            return panel;
        }

        protected void Fail(EdenApiError e, string context)
        {
            App.Busy(false);
            App.ShowError(e, context);
        }
    }
}
