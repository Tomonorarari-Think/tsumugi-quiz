using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.UI.Views.Settings
{
    /// <summary>
    /// Settings View（issue #28）のルーム設定タブで使う列挙型フィールド（<c>host.role</c> /
    /// <c>questions.typeFilter</c> / <c>score.penaltyType</c>）の「保存値（docs/room-settings.md §1 の
    /// キー値）⇔ 画面表示用ラベル（日本語）」の対応表。<c>DropdownField.choices</c> にはラベルを渡し、
    /// 選択されたラベルから <see cref="RoomSettingsValidator"/> が読む生の値へ戻すのに使う。
    /// </summary>
    /// <remarks>
    /// Unity API に依存しない純 C#（<see cref="DropdownOption"/> はタプルではなく明示的な型にして
    /// 可読性を優先した）。<see cref="LabelFor"/> / <see cref="ValueFor"/> は未知の値・ラベルを渡されても
    /// 例外にせず先頭の選択肢にフォールバックする（境界での防御的な実装）。
    /// </remarks>
    internal static class RoomSettingsDropdownOptions
    {
        /// <summary><c>host.role</c> の選択肢（保存値は <see cref="HostRoles"/> のキーと同じ）。</summary>
        public static readonly DropdownOption[] HostRole =
        {
            new DropdownOption(HostRoles.PlayerKey, HostRoles.ToDisplayName(TsumugiQuiz.Core.Network.HostRole.Player)),
            new DropdownOption(HostRoles.ModeratorKey, HostRoles.ToDisplayName(TsumugiQuiz.Core.Network.HostRole.Moderator)),
        };

        /// <summary><c>questions.typeFilter</c> の選択肢（docs/room-settings.md §1「問題選択」）。</summary>
        public static readonly DropdownOption[] QuestionsTypeFilter =
        {
            new DropdownOption("both", "自由入力・選択式の両方"),
            new DropdownOption("freeText", "自由入力のみ"),
            new DropdownOption("choice", "選択式のみ"),
        };

        /// <summary><c>score.penaltyType</c> の選択肢（docs/room-settings.md §1「得点」）。</summary>
        public static readonly DropdownOption[] ScorePenaltyType =
        {
            new DropdownOption("skipNext", "次の問題は休み"),
            new DropdownOption("minusPoints", "減点"),
            new DropdownOption("none", "ペナルティなし"),
        };

        /// <summary>保存値からラベルを引く。一致しなければ先頭の選択肢のラベル（空配列なら空文字列）。</summary>
        public static string LabelFor(DropdownOption[] options, string value)
        {
            foreach (var option in options)
            {
                if (option.Value == value)
                {
                    return option.Label;
                }
            }

            return options.Length > 0 ? options[0].Label : string.Empty;
        }

        /// <summary>ラベルから保存値を引く。一致しなければ先頭の選択肢の値（空配列なら空文字列）。</summary>
        public static string ValueFor(DropdownOption[] options, string label)
        {
            foreach (var option in options)
            {
                if (option.Label == label)
                {
                    return option.Value;
                }
            }

            return options.Length > 0 ? options[0].Value : string.Empty;
        }

        /// <summary>選択肢一覧（<c>DropdownField.choices</c> に渡す表示ラベルの一覧）を作る。</summary>
        public static System.Collections.Generic.List<string> LabelsOf(DropdownOption[] options)
        {
            var labels = new System.Collections.Generic.List<string>(options.Length);
            foreach (var option in options)
            {
                labels.Add(option.Label);
            }

            return labels;
        }
    }

    /// <summary>保存値と表示ラベルの組。</summary>
    internal readonly struct DropdownOption
    {
        public DropdownOption(string value, string label)
        {
            Value = value;
            Label = label;
        }

        /// <summary>docs/room-settings.md のキー値（JSON・<see cref="TsumugiQuiz.Room.RoomSettingsInput"/> と同じ表記）。</summary>
        public string Value { get; }

        /// <summary>画面表示用の日本語ラベル。</summary>
        public string Label { get; }
    }
}
