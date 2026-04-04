using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using ZUI.Services;

namespace ZUI.Services
{
	public class WinwsService
	{
		private Process? _process;
		private DispatcherQueue? _dispatcherQueue;

		public bool IsRunning => _process != null && !_process.HasExited;
		public event Action<string>? LogReceived;
		public event Action<bool>? StatusChanged;

		public void SetDispatcherQueue(DispatcherQueue queue)
		{
			_dispatcherQueue = queue;
		}

		private void PlaySound(Microsoft.UI.Xaml.ElementSoundKind kind)
		{
			if (!AppSettings.SoundEffects) return;
			_dispatcherQueue?.TryEnqueue(() =>
				Microsoft.UI.Xaml.ElementSoundPlayer.Play(kind));
		}

		public async Task StartAsync(string arguments)
		{
			if (IsRunning) return;

			_process = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = ZapretPaths.WinwsExe,
					Arguments = arguments,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true,
					WorkingDirectory = ZapretPaths.WinwsDir
				}
			};

			_process.OutputDataReceived += (s, e) => { if (e.Data != null) LogReceived?.Invoke(e.Data); };
			_process.ErrorDataReceived += (s, e) => { if (e.Data != null) LogReceived?.Invoke(e.Data); };

			_process.Start();
			_process.BeginOutputReadLine();
			_process.BeginErrorReadLine();

			StatusChanged?.Invoke(true);
			PlaySound(Microsoft.UI.Xaml.ElementSoundKind.Invoke);

			if (ToastNotifier.IsEnabled)
			{
				ToastNotifier.Show(
					"Сервис запущен",
					"Z-UI успешно запущен",
					ToastType.Success);
			}

			await Task.CompletedTask;
		}

	public void Stop()
	{
		if (_process == null) return;

		try
		{
			_process.Kill(true);
			_process.WaitForExit(10000);
		}
		catch { }

		StopWinDivertServices();

		_process = null;
        StatusChanged?.Invoke(false);
        PlaySound(Microsoft.UI.Xaml.ElementSoundKind.Hide);

        if (ToastNotifier.IsEnabled)
        {
            ToastNotifier.Show(
                LocalizationService.Get("ServiceStopped"),
                LocalizationService.Get("ZuiStopped"),
                ToastType.Informational);
        }
    }

	private void StopWinDivertServices()
	{
		try
		{
			var psi = new ProcessStartInfo
			{
				FileName = "sc.exe",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true
			};

			psi.Arguments = "stop WinDivert";
			using (var result = Process.Start(psi))
			{
				result?.WaitForExit(3000);
			}

			for (int i = 0; i < 10; i++)
			{
				System.Threading.Thread.Sleep(500);
				psi.Arguments = "query WinDivert";
				using (var result = Process.Start(psi))
				{
					if (result == null) break;
					var output = result.StandardOutput.ReadToEnd();
					if (output.Contains("STOPPED") || output.Contains("не найдена") || output.Contains("1060"))
						break;
				}
			}

			psi.Arguments = "delete WinDivert";
			using (var result = Process.Start(psi))
			{
				result?.WaitForExit(3000);
			}
		}
		catch { }

		try
		{
			var psi = new ProcessStartInfo
			{
				FileName = "sc.exe",
				UseShellExecute = false,
				CreateNoWindow = true
			};

			psi.Arguments = "stop WinDivert14";
			using (var result = Process.Start(psi))
			{
				result?.WaitForExit(3000);
			}

			psi.Arguments = "delete WinDivert14";
			using (var result = Process.Start(psi))
			{
				result?.WaitForExit(3000);
			}
		}
		catch { }
	}
	}
}
