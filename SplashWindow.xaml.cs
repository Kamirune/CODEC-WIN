using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CodecWpf
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            Loaded += SplashWindow_Loaded;
        }

        private void SplashWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            // 1) 로고 페이드 + 살짝 확대 (0 ~ 0.7초)
            LogoPanel.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.7)) { EasingFunction = ease });
            LogoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.8, 1, TimeSpan.FromSeconds(0.7)) { EasingFunction = ease });
            LogoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.8, 1, TimeSpan.FromSeconds(0.7)) { EasingFunction = ease });

            // 2) 태그라인 등장 (0.6초 지연)
            Tagline.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.6))
                { BeginTime = TimeSpan.FromSeconds(0.6), EasingFunction = ease });

            // 3) 상태 텍스트 등장 (0.9초 지연)
            BootText.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5))
                { BeginTime = TimeSpan.FromSeconds(0.9) });

            // 4) 진행 바 채우기 (0.5 ~ 2.6초, 폭 0 → 300)
            ProgressBar.BeginAnimation(WidthProperty,
                new DoubleAnimation(0, 300, TimeSpan.FromSeconds(2.1))
                { BeginTime = TimeSpan.FromSeconds(0.5) });

            // 5) 상태 텍스트 단계별 교체
            SwitchTextAt(1.6, "LOADING MODULES...");
            SwitchTextAt(2.4, "READY ✓");

            // 6) 전체 끝나면 페이드아웃 → 메인 창 열기 (약 2.9초)
            var done = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.9) };
            done.Tick += (s, ev) =>
            {
                done.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.4));
                fade.Completed += (s2, ev2) =>
                {
                    var main = new MainWindow();
                    Application.Current.MainWindow = main;
                    // 이제부터는 메인 창을 닫으면 앱이 종료되도록 되돌린다.
                    Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    main.Show();
                    Close();
                };
                BeginAnimation(OpacityProperty, fade);
            };
            done.Start();
        }

        // 지정 시각에 상태 텍스트 교체
        private void SwitchTextAt(double seconds, string text)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                BootText.Text = text;
            };
            t.Start();
        }
    }
}
