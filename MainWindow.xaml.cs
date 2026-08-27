using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CodecWpf
{
    public partial class MainWindow : Window
    {
        private static readonly Brush CyanBrush =
            new SolidColorBrush(Color.FromRgb(0x22, 0xE9, 0xE9));
        private static readonly Brush ErrorBrush =
            new SolidColorBrush(Color.FromRgb(0xF0, 0x3C, 0xB4));

        public MainWindow()
        {
            InitializeComponent();

            foreach (var m in Codec.Methods)
                MethodBox.Items.Add(m);
            MethodBox.SelectedIndex = 0;   // base64

            UpdateButtons(false);   // 출력이 비어 있으므로 시작 시 비활성
        }

        private bool IsEncode => EncodeMode.IsChecked == true;
        private string CurrentMethod => MethodBox.SelectedItem as string ?? "base64";

        private int GetShift()
        {
            return int.TryParse(ShiftBox.Text.Trim(), out int s) ? s : 3;
        }

        // 방식 바뀌면: shift 칸 표시 여부 + 중앙 라벨 갱신 후 재변환
        private void Method_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ShiftPanel == null) return;   // 초기화 중 호출 방지

            ShiftPanel.Visibility = CurrentMethod == "caesar"
                ? Visibility.Visible : Visibility.Collapsed;

            Convert();
        }

        private void Mode_Changed(object sender, RoutedEventArgs e) => Convert();
        private void Input_Changed(object sender, EventArgs e) => Convert();

        // 실시간 변환 핵심
        private void Convert()
        {
            if (OutputBox == null || InputBox == null) return;

            // placeholder 힌트 표시/숨김
            if (InputHint != null)
                InputHint.Visibility = string.IsNullOrEmpty(InputBox.Text)
                    ? Visibility.Visible : Visibility.Collapsed;

            // 중앙 방향 라벨 갱신
            if (ArrowText != null)
            {
                string arrow = IsEncode ? "▼  {0} ▼" : "▲  {0} ▲";
                ArrowText.Text = string.Format(arrow, CurrentMethod.ToUpper() + "  ");
            }

            string input = InputBox.Text;
            if (string.IsNullOrEmpty(input))
            {
                OutputBox.Text = "";
                SetStatus("ready", false);
                UpdateButtons(false);
                return;
            }

            try
            {
                string result = IsEncode
                    ? Codec.Encode(CurrentMethod, input, GetShift())
                    : Codec.Decode(CurrentMethod, input, GetShift());

                bool changed = OutputBox.Text != result;
                OutputBox.Foreground = CyanBrush;
                OutputBox.Text = result;
                SetStatus($"{(IsEncode ? "encoded" : "decoded")} · {result.Length} chars", false);
                UpdateButtons(!string.IsNullOrEmpty(result));

                if (changed && !string.IsNullOrEmpty(result))
                    PulseOutputGlow();
            }
            catch (Exception ex)
            {
                OutputBox.Foreground = ErrorBrush;
                OutputBox.Text = "✗ " + ex.Message;
                SetStatus("입력 형식을 확인하세요", true);
                UpdateButtons(false);   // 에러 결과는 복사/스왑 대상 아님
            }
        }

        // 변환 성공 시 출력 패널이 잠깐 시안으로 빛났다 사라진다.
        private void PulseOutputGlow()
        {
            if (OutGlow == null || OutBorderBrush == null) return;

            // 바깥 글로우: 0 → 0.55 → 0
            var glow = new DoubleAnimationUsingKeyFrames();
            glow.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            glow.KeyFrames.Add(new LinearDoubleKeyFrame(0.55, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.12))));
            glow.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.7))));
            OutGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, glow);

            // 테두리: 기본색 → 시안 → 기본색
            var border = new ColorAnimationUsingKeyFrames();
            border.KeyFrames.Add(new LinearColorKeyFrame(Color.FromRgb(0x2A, 0x25, 0x40), KeyTime.FromTimeSpan(TimeSpan.Zero)));
            border.KeyFrames.Add(new LinearColorKeyFrame(Color.FromRgb(0x22, 0xE9, 0xE9), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.12))));
            border.KeyFrames.Add(new LinearColorKeyFrame(Color.FromRgb(0x2A, 0x25, 0x40), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.7))));
            OutBorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, border);
        }

        // 출력이 유효할 때만 COPY / SWAP 활성화
        private void UpdateButtons(bool hasValidOutput)
        {
            if (CopyBtn != null) CopyBtn.IsEnabled = hasValidOutput;
            if (SwapBtn != null) SwapBtn.IsEnabled = hasValidOutput;
        }

        private void SetStatus(string msg, bool error)
        {
            if (StatusText == null) return;
            StatusText.Text = msg;
            StatusText.Foreground = error ? ErrorBrush
                : new SolidColorBrush(Color.FromRgb(0x6C, 0x6A, 0x86));
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(OutputBox.Text))
            {
                Clipboard.SetText(OutputBox.Text);
                SetStatus("클립보드에 복사됨 ✓", false);
            }
        }

        // 출력 → 입력으로 보내고 인코딩/디코딩 모드를 뒤집는다.
        // (예: base64 인코딩 결과를 그대로 디코딩 입력으로 확인)
        private void Swap_Click(object sender, RoutedEventArgs e)
        {
            string output = OutputBox.Text;
            if (string.IsNullOrEmpty(output) || output.StartsWith("✗"))
                return;

            // 모드 뒤집기 (Checked 이벤트가 Convert를 부르므로 입력은 그 뒤에 넣는다)
            if (IsEncode) DecodeMode.IsChecked = true;
            else EncodeMode.IsChecked = true;

            InputBox.Text = output;                 // TextChanged → Convert 자동 실행
            InputBox.CaretIndex = InputBox.Text.Length;
            InputBox.Focus();
            SetStatus("입력 ↔ 출력 전환됨", false);
        }
    }
}
