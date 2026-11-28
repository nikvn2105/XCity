using System;
using UnityEngine;
using UnityEngine.UI;

namespace XCity
{
    // Budget panel, opened by tapping the money stat: income per zone with its tax rate, spending per
    // line with funding for the five services. Changes apply from the next tick.
    public partial class XCityBootstrap
    {
        private const int FundingStep = 10;

        private GameObject _budgetPanel;
        private Text _budgetSummary;
        private readonly Text[] _taxRateTexts = new Text[3];
        private readonly Text[] _taxAmountTexts = new Text[3];
        private readonly Text[] _fundingTexts = new Text[5];
        private readonly Text[] _spendingTexts = new Text[9];

        private bool BudgetOpen => _budgetPanel != null && _budgetPanel.activeSelf;

        private void ToggleBudget()
        {
            CancelGesture();
            CloseBuildPicker();
            if (_budgetPanel == null) BuildBudgetPanel();
            else _budgetPanel.SetActive(!_budgetPanel.activeSelf);
            if (BudgetOpen)
            {
                _budgetPanel.transform.SetAsLastSibling();
                RefreshBudgetPanel();
            }
        }

        private void BuildBudgetPanel()
        {
            Font font = UiTheme.Font;
            _budgetPanel = CreatePanel(_safeAreaRoot, "Budget", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-390, -215), new Vector2(390, 205), new Color(UiTheme.Panel.r, UiTheme.Panel.g, UiTheme.Panel.b, 0.94f));
            UiTheme.Float(_budgetPanel.GetComponent<Image>(), 18f);

            Text title = CreateLabel(_budgetPanel.transform, font, UiTheme.TitleSize, UiTheme.Text, TextAnchor.MiddleLeft);
            Place(title.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(20, -52), new Vector2(-70, -12));
            title.fontStyle = FontStyle.Bold;
            title.text = UiText.Budget;
            _budgetSummary = CreateLabel(_budgetPanel.transform, font, UiTheme.SmallSize, UiTheme.TextSoft, TextAnchor.MiddleRight);
            Place(_budgetSummary.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(150, -52), new Vector2(-70, -12));
            _budgetSummary.supportRichText = true;

            Button close = CreateIconButton(_budgetPanel.transform, UiIcon.Close, "CloseBudget", 20);
            Place((RectTransform)close.transform, Vector2.one, Vector2.one,
                new Vector2(-UiTheme.MinTouch - 12, -UiTheme.MinTouch - 10), new Vector2(-12, -10));
            close.onClick.AddListener(ToggleBudget);

            Transform income = CreateBudgetColumn(new Vector2(0, 0), new Vector2(0.5f, 1), new Vector2(20, 16), new Vector2(-10, -62), UiText.Income);
            Transform spending = CreateBudgetColumn(new Vector2(0.5f, 0), Vector2.one, new Vector2(10, 16), new Vector2(-20, -62), UiText.Spending);

            Color[] zoneColors = { UiTheme.Residential, UiTheme.Commercial, UiTheme.Industrial };
            for (int i = 0; i < 3; i++)
            {
                var zone = (TaxZone)i;
                _taxAmountTexts[i] = AddBudgetRow(income, UiText.TaxZones[i], zoneColors[i],
                    () => ChangeTax(zone, -1), () => ChangeTax(zone, 1), out _taxRateTexts[i]);
            }

            Color[] lineColors =
            {
                UiTheme.RoadGray, UiTheme.Utilities, UiTheme.Education, UiTheme.Services,
                UiTheme.Commercial, UiTheme.Demolish, UiTheme.Residential, UiTheme.TextMuted, UiTheme.TextMuted
            };
            for (int i = 0; i < _spendingTexts.Length; i++)
            {
                int service = i - 2; // lines 2-6 are the funded services
                bool funded = service >= 0 && service < _fundingTexts.Length;
                Text rate = null;
                _spendingTexts[i] = funded
                    ? AddBudgetRow(spending, UiText.SpendingLines[i], lineColors[i],
                        () => ChangeFunding((BudgetService)service, -FundingStep),
                        () => ChangeFunding((BudgetService)service, FundingStep), out rate)
                    : AddBudgetRow(spending, UiText.SpendingLines[i], lineColors[i], null, null, out rate);
                if (funded) _fundingTexts[service] = rate;
            }
        }

        private Transform CreateBudgetColumn(Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax, string heading)
        {
            var column = new GameObject("Column", typeof(RectTransform));
            var rect = (RectTransform)column.transform;
            rect.SetParent(_budgetPanel.transform, false);
            Place(rect, min, max, offsetMin, offsetMax);
            var layout = column.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 1;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            Text label = CreateLabel(column.transform, UiTheme.Font, UiTheme.TinySize, UiTheme.TextMuted, TextAnchor.MiddleLeft);
            label.fontStyle = FontStyle.Bold;
            label.text = heading;
            label.gameObject.AddComponent<LayoutElement>().minHeight = 24f;
            return column.transform;
        }

        // A line: colour dot, name, optional −/rate/+ controls, amount. Returns the amount text.
        private Text AddBudgetRow(Transform column, string name, Color dot, Action minus, Action plus, out Text rate)
        {
            var row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(column, false);
            row.AddComponent<LayoutElement>().minHeight = minus != null ? UiTheme.MinTouch : 20f;
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var swatch = CreatePanel(row.transform, "Dot", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, dot);
            var swatchSize = swatch.AddComponent<LayoutElement>();
            swatchSize.minWidth = swatchSize.preferredWidth = swatchSize.minHeight = swatchSize.preferredHeight = 10f;
            UiTheme.Round(swatch.GetComponent<Image>(), 5f, outline: false);

            Text label = CreateLabel(row.transform, UiTheme.Font, UiTheme.SmallSize, UiTheme.Text, TextAnchor.MiddleLeft);
            label.text = name;
            label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            rate = null;
            if (minus != null)
            {
                AddStepButton(row.transform, "−", minus);
                rate = CreateLabel(row.transform, UiTheme.Font, UiTheme.SmallSize, UiTheme.Text, TextAnchor.MiddleCenter);
                rate.fontStyle = FontStyle.Bold;
                rate.gameObject.AddComponent<LayoutElement>().preferredWidth = 48f;
                AddStepButton(row.transform, "+", plus);
            }

            Text amount = CreateLabel(row.transform, UiTheme.Font, UiTheme.SmallSize, UiTheme.TextSoft, TextAnchor.MiddleRight);
            amount.gameObject.AddComponent<LayoutElement>().preferredWidth = 74f;
            return amount;
        }

        private void AddStepButton(Transform row, string label, Action action)
        {
            Button button = CreateHudButton(row, UiTheme.Font, label, "Step");
            var size = button.gameObject.AddComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = 40f;
            size.minHeight = size.preferredHeight = 38f;
            button.onClick.AddListener(() => action());
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private void ChangeTax(TaxZone zone, int delta)
        {
            _city.SetTaxRate(zone, _city.GetTaxRate(zone) + delta);
            _cityDirty = true;
            _audio?.PlayBuild();
            RefreshBudgetPanel();
        }

        private void ChangeFunding(BudgetService service, int delta)
        {
            _city.SetFunding(service, _city.GetFunding(service) + delta);
            _cityDirty = true;
            _audio?.PlayBuild();
            RefreshHUD(); // reach changed: refresh overlays and the info panel too
        }

        private void RefreshBudgetPanel()
        {
            if (!BudgetOpen) return;
            BudgetReport budget = _city.LastBudget;
            int[] taxes = { budget.ResidentialTax, budget.CommercialTax, budget.IndustrialTax };
            for (int i = 0; i < 3; i++)
            {
                _taxRateTexts[i].text = $"{_city.GetTaxRate((TaxZone)i)}%";
                _taxAmountTexts[i].text = $"${taxes[i]:N0}";
            }
            for (int i = 0; i < _fundingTexts.Length; i++)
                _fundingTexts[i].text = $"{_city.GetFunding((BudgetService)i)}%";
            int[] lines = { budget.Roads, budget.Power, budget.Education, budget.Health, budget.Police, budget.Fire, budget.Garbage, budget.Other, budget.Loan };
            for (int i = 0; i < lines.Length; i++)
                _spendingTexts[i].text = $"${lines[i]:N0}";

            int net = budget.Income - budget.Spending;
            string netColor = net < 0 ? "#ff7a7a" : "#5fe09a";
            _budgetSummary.text = $"{UiText.Income} ${budget.Income:N0} · {UiText.Spending} ${budget.Spending:N0} · " +
                $"<color={netColor}><b>{net:+#,##0;-#,##0;0}</b></color>{UiText.PerTick}";
        }
    }
}
