using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace WpfTest
{
    /// <summary>
    /// 全局异常处理：
    /// 捕获 UI 线程、非 UI 线程和 Task 中未处理的异常，
    /// 记录日志并提示用户，防止程序直接崩溃。
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // UI 线程未处理异常（如 DispatcherTimer 回调、按钮事件中抛出的异常）
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // 非 UI 线程未处理异常（如 Task.Run 中抛出且未 catch 的异常）
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // Task 中未被观察到的异常（fire-and-forget 的 async void / Task 异常）
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        /// <summary>
        /// UI 线程异常：记录 + 弹窗提示，标记 Handled 防止程序崩溃
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Debug.WriteLine($"[UI线程异常] {e.Exception}");
            MessageBox.Show(
                $"程序发生异常：\n{e.Exception.Message}\n\n详细信息已输出到调试窗口，程序将继续运行。",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        /// <summary>
        /// 非 UI 线程异常：记录 + 弹窗提示
        /// </summary>
        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Debug.WriteLine($"[非UI线程异常] isTerminating={e.IsTerminating}\n{ex}");
                MessageBox.Show(
                    $"后台线程发生异常：\n{ex.Message}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Task 未观察异常：记录并标记为已观察，防止终结器时进程被终止
        /// </summary>
        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Debug.WriteLine($"[Task未观察异常] {e.Exception}");
            e.SetObserved();
        }
    }
}
