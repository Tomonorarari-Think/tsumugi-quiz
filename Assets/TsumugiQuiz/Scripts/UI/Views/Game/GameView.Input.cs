using UnityEngine.InputSystem;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// <see cref="GameView"/> のうち、早押しキー（Input System、既定 Space、仮決め K21・requirements.md FR-20）
    /// の割り当てを扱う部分（issue #14）。
    /// </summary>
    /// <remarks>
    /// キー割り当ての変更（Settings 画面）は本 issue の範囲外のため、<c>&lt;Keyboard&gt;/space</c> に
    /// 固定でバインドする。判定そのもの（フェーズ・二重押下防止）は
    /// <see cref="GameView.TryBuzz"/>（<c>GameSession.RequestBuzz()</c> 経由）で、
    /// 早押しボタンのクリックと同じ経路を通る。
    /// </remarks>
    public sealed partial class GameView
    {
        private InputAction _buzzInputAction;

        private void EnableBuzzInput()
        {
            _buzzInputAction = new InputAction(
                name: "GameView.Buzz",
                type: InputActionType.Button,
                binding: "<Keyboard>/space");
            _buzzInputAction.performed += OnBuzzInputPerformed;
            _buzzInputAction.Enable();
        }

        private void DisableBuzzInput()
        {
            if (_buzzInputAction == null)
            {
                return;
            }

            _buzzInputAction.performed -= OnBuzzInputPerformed;
            _buzzInputAction.Disable();
            _buzzInputAction.Dispose();
            _buzzInputAction = null;
        }

        private void OnBuzzInputPerformed(InputAction.CallbackContext context) => TryBuzz();
    }
}
