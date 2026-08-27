using System.Windows;

namespace CodecWpf
{
    public partial class App : Application
    {
        // 스플래시 창을 먼저 띄운다. 메인 창은 스플래시가 끝나면 스스로 연다.
        private void App_Startup(object sender, StartupEventArgs e)
        {
            // 스플래시가 닫혀도 앱이 꺼지지 않도록: 메인 창을 연 뒤 기본 종료 모드로 되돌린다.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var splash = new SplashWindow();
            splash.Show();
        }
    }
}
