using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace CodecWpf
{
    public partial class ToolsWindow : Window
    {
        private static readonly Brush CyanBrush =
            new SolidColorBrush(Color.FromRgb(0x22, 0xE9, 0xE9));
        private static readonly Brush ErrorBrush =
            new SolidColorBrush(Color.FromRgb(0xF0, 0x3C, 0xB4));
        private static readonly Brush DimBrush =
            new SolidColorBrush(Color.FromRgb(0x8A, 0x86, 0xB8));

        // 이보다 큰 파일은 화면에 띄우지 않고 바로 .txt로 저장 (TextBox가 너무 느려짐)
        private const long MaxDisplayBytes = 5 * 1024 * 1024;

        private bool _updatingRadix;   // 진법 칸끼리 서로 갱신할 때 무한 반복 방지

        public ToolsWindow()
        {
            InitializeComponent();
        }

        // ── 탭 전환 ─────────────────────────────────────
        private void Tab_Changed(object sender, RoutedEventArgs e)
        {
            if (RadixPanel == null) return;   // 초기화 중 (XAML의 IsChecked="True")

            string tab = (string)((RadioButton)sender).Tag;
            RadixPanel.Visibility = tab == "radix" ? Visibility.Visible : Visibility.Collapsed;
            IpPanel.Visibility    = tab == "ip"    ? Visibility.Visible : Visibility.Collapsed;
            TimePanel.Visibility  = tab == "time"  ? Visibility.Visible : Visibility.Collapsed;
            FilePanel.Visibility  = tab == "file"  ? Visibility.Visible : Visibility.Collapsed;
            SetStatus("ready", false);

            // 탭에 들어가면 입력칸에 바로 커서
            Dispatcher.BeginInvoke(new Action(() =>
            {
                switch (tab)
                {
                    case "radix": RadixDec.Focus(); break;
                    case "ip":    IpInput.Focus(); break;
                    case "time":  TimeInput.Focus(); break;
                }
            }));
        }

        private void SetStatus(string msg, bool error)
        {
            if (StatusText == null) return;
            StatusText.Text = msg;
            StatusText.Foreground = error ? ErrorBrush : DimBrush;
        }

        // ── 1) 진법 변환 ────────────────────────────────
        private void Radix_Changed(object sender, TextChangedEventArgs e)
        {
            if (_updatingRadix || RadixHex == null) return;

            var src = (TextBox)sender;
            int radix = int.Parse((string)src.Tag);
            var all = new[] { RadixBin, RadixOct, RadixDec, RadixHex };

            _updatingRadix = true;
            try
            {
                if (string.IsNullOrWhiteSpace(src.Text))
                {
                    foreach (var box in all.Where(b => b != src)) box.Text = "";
                    SetStatus("ready", false);
                    return;
                }

                var value = Tools.ParseRadix(src.Text, radix);
                foreach (var box in all.Where(b => b != src))
                    box.Text = Tools.ToRadix(value, int.Parse((string)box.Tag));

                long bits = value.IsZero ? 1 : (long)Math.Ceiling(System.Numerics.BigInteger.Log(
                    System.Numerics.BigInteger.Abs(value) + 1, 2));
                SetStatus($"{radix}진수 입력 · {bits}비트", false);
            }
            catch (FormatException ex)
            {
                SetStatus("✗ " + ex.Message, true);   // 다른 칸은 마지막 정상 값 유지
            }
            finally
            {
                _updatingRadix = false;
            }
        }

        // ── 2) IP / 3) 타임스탬프: 입력 → 결과 보고서 ─────
        private void Ip_Changed(object sender, TextChangedEventArgs e) =>
            ShowReport(IpInput.Text, IpOutput, Tools.IpReport);

        private void Time_Changed(object sender, TextChangedEventArgs e) =>
            ShowReport(TimeInput.Text, TimeOutput, s => Tools.TimestampReport(s, TimeZoneInfo.Local));

        private void ShowReport(string input, TextBox output, Func<string, string> make)
        {
            if (output == null) return;
            if (string.IsNullOrWhiteSpace(input))
            {
                output.Text = "";
                SetStatus("ready", false);
                return;
            }
            try
            {
                output.Foreground = CyanBrush;
                output.Text = make(input);
                SetStatus("변환됨", false);
            }
            catch (FormatException ex)
            {
                output.Foreground = ErrorBrush;
                output.Text = "✗ " + ex.Message;
                SetStatus("입력 형식을 확인하세요", true);
            }
        }

        private void Now_Click(object sender, RoutedEventArgs e)
        {
            TimeInput.Text = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            TimeInput.CaretIndex = TimeInput.Text.Length;
            TimeInput.Focus();
        }

        // ── 4) 파일 ↔ Base64 ────────────────────────────
        private void File_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropBorder.Color = Color.FromRgb(0x22, 0xE9, 0xE9);   // 드롭 가능 표시
                e.Handled = true;
            }
        }

        private void File_PreviewDragLeave(object sender, DragEventArgs e) =>
            DropBorder.Color = Color.FromRgb(0x3A, 0x35, 0x60);

        private async void File_PreviewDrop(object sender, DragEventArgs e)
        {
            DropBorder.Color = Color.FromRgb(0x3A, 0x35, 0x60);
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Handled = true;

            var file = ((string[])e.Data.GetData(DataFormats.FileDrop)).FirstOrDefault(File.Exists);
            if (file == null)
            {
                SetStatus("폴더는 변환할 수 없어요. 파일 하나를 놓아주세요", true);
                return;
            }
            await EncodeFileAsync(file);
        }

        private async void PickFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Base64로 변환할 파일 선택" };
            if (dlg.ShowDialog(this) == true)
                await EncodeFileAsync(dlg.FileName);
        }

        private async Task EncodeFileAsync(string path)
        {
            try
            {
                var info = new FileInfo(path);
                SetStatus($"변환 중 · {info.Name} ({Tools.HumanSize(info.Length)})", false);

                string b64 = await Task.Run(() => Convert.ToBase64String(File.ReadAllBytes(path)));

                if (info.Length > MaxDisplayBytes)
                {
                    // 큰 파일은 화면 대신 바로 텍스트 파일로 저장
                    var dlg = new SaveFileDialog
                    {
                        Title = "Base64 결과 저장 (파일이 커서 화면에 표시하지 않음)",
                        FileName = info.Name + ".b64.txt",
                        Filter = "텍스트 파일|*.txt|모든 파일|*.*"
                    };
                    if (dlg.ShowDialog(this) == true)
                    {
                        await Task.Run(() => File.WriteAllText(dlg.FileName, b64));
                        SetStatus($"{info.Name} ({Tools.HumanSize(info.Length)}) → {Path.GetFileName(dlg.FileName)} 저장 완료", false);
                    }
                    else SetStatus("저장을 취소했어요", false);
                    return;
                }

                B64Box.Text = b64;
                SetStatus($"{info.Name} ({Tools.HumanSize(info.Length)}) → Base64 {b64.Length:N0}자", false);
            }
            catch (Exception ex)
            {
                SetStatus("✗ 파일을 읽을 수 없어요: " + ex.Message, true);
            }
        }

        private void SaveFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(B64Box.Text))
            {
                SetStatus("저장할 Base64가 없어요. 아래 칸에 붙여넣으세요", true);
                return;
            }
            try
            {
                byte[] data = Tools.ParseBase64Payload(B64Box.Text, out string ext);
                var dlg = new SaveFileDialog
                {
                    Title = "Base64를 파일로 저장",
                    FileName = "decoded" + ext,
                    Filter = "모든 파일|*.*"
                };
                if (dlg.ShowDialog(this) != true) return;

                File.WriteAllBytes(dlg.FileName, data);
                SetStatus($"{Path.GetFileName(dlg.FileName)} ({Tools.HumanSize(data.Length)}) 저장 완료 · 추정 형식 {ext}", false);
            }
            catch (FormatException ex)
            {
                SetStatus("✗ " + ex.Message, true);
            }
            catch (Exception ex)
            {
                SetStatus("✗ 저장 실패: " + ex.Message, true);
            }
        }

        private void CopyB64_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(B64Box.Text)) return;
            Clipboard.SetText(B64Box.Text);
            SetStatus("클립보드에 복사됨 ✓", false);
        }

        private void ClearB64_Click(object sender, RoutedEventArgs e)
        {
            B64Box.Clear();
            SetStatus("ready", false);
        }
    }
}
