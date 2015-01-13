﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

using System.Windows.Forms;

namespace KeyboardExtending {

	#region Consts and Enums
	public enum KeyAction {
		KeyDown = 0x0100,
		KeyUp = 0x0101
	}
	#endregion

	#region Delegate Types and their arguments types
	public delegate void KeyActionHandler(IntPtr hookID, KeyActionArgs e);
	public delegate bool KeyActionHandlerEx(IntPtr hookID, KeyActionArgs e);
	/// <summary>
	/// Структура переменной с информацией о клавиатурном событии.
	/// </summary>
	public struct KeyActionArgs {
		public KeyActionArgs(Keys keyCode, int keyAction) { KeyCode = keyCode; KeyAction = keyAction; }
		public KeyActionArgs(int keyCode, int keyAction) { KeyCode = (Keys)keyCode; KeyAction = keyAction; }
		public Keys KeyCode;
		public int KeyAction;
	}
	#endregion

	public class KeyHooker {
		#region Constants
		#region keyActionCodes
		private const int WH_KEYBOARD_LL = 13; // Номер прерывания, как йа понимаю. А может быть, некий уровень/способ вмешательства в работу системы. Используется при назначении обработчика (Вызове SetWindowsHookEx).
		#endregion
		#endregion

		#region Parameters
		private static IntPtr _hookID = IntPtr.Zero; // Номер нашего прерывателя. Инициализировали нулём.
		#endregion

		#region DLL signatures and API-hooking-related types
		// Тут пиздец. Это сигнатуры использования библиотек, йа ещё не проникся их логикой. 
		// Этакое объявление о том, что в такой-то библиотеке есть такая-то функция, которая принимает такие-то аргументы.
		// Собсвенно, этот код позволяет этими самыми функциями пользоваться. На системе, где этих бибилиотек нет, ничего не выйдет хорошего.
		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		// АПИшная Назначалка обработчика.
		private static extern IntPtr SetWindowsHookEx(int idHook,
			LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool UnhookWindowsHookEx(IntPtr hhk);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
			IntPtr wParam, IntPtr lParam);

		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern IntPtr GetModuleHandle(string lpModuleName);

		private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam); // Тип делегата для обработки события клавиатуры
		#endregion

		#region Hooking
		/// <summary>
		///  Назначатель обработчика
		/// </summary>
		/// <param name="proc">Некий держатель функции. Ему мы заранее присвоили функцию HookCallback()</param>
		/// <returns>Вроде, возвращает ID прерывателя... Или прерывания. Хз ваще, надо экспериментировать или читать где-то.
		/// Важно, что возвращаемый идентификатор потом можно использовать для отключения перехвата.</returns>
		private static IntPtr SetHook(LowLevelKeyboardProc proc) {
			using (Process curProcess = Process.GetCurrentProcess())
			using (ProcessModule curModule = curProcess.MainModule) {
				return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
			}
		}

		/// <summary>
		/// Задаваемая Функция - обработчик нажатия кнопки. Вызывается в прерывании.
		/// "отклик на действие с клавишей"
		/// </summary>
		/// <param name="nCode">Некий код, который если менее нуля, то всё плохо</param>
		/// <param name="wParam">Действие над клавишей</param>
		/// <param name="lParam">Код клавиши</param>
		/// <returns></returns>
		private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
			int keyCode;
			int keyAction;

			if (nCode >= 0) {
				keyCode = Marshal.ReadInt32(lParam);
				keyAction = (int)wParam;
			}
			else {
				return CallNextHookEx(_hookID, nCode, wParam, lParam);
			}

			bool block = false;
			if (OnKeyActionEx != null)
				block = OnKeyActionEx(_hookID, new KeyActionArgs(keyCode, keyAction));

			if (block)
				return new IntPtr(1);

			if (OnKeyAction != null)
				OnKeyAction(_hookID, new KeyActionArgs(keyCode, keyAction));

			return CallNextHookEx(_hookID, nCode, wParam, lParam); // Передаём управление следующему обработчику в системе
		}
		#endregion

		#region Events
		/// <summary>
		/// Стреляет, при попадании евента в Hooker. Позволяет реагировать, но не блокировать евент.
		/// В качестве определителя источника передаётся ID-указатель перехватывания. Можете составлять себе словари для них, если очень хочется несколько хуков.
		/// </summary>
		public event KeyActionHandler OnKeyAction;
		/// <summary>
		/// Подписка на это событе позволяет ещё и указать, должен ли Hooker заблокировать дальнейшую обработку евента системой
		/// Верните 1(true), чтобы заблокировать евент. 0(false) - пропустить далее.
		/// ВАЖНО: Hooker не станет вызывать OnKeyAction, если будет возвращено требование блокирования.
		/// Нельзя заблокировать Ctrl+Alt+Del.
		/// В качестве определителя источника передаётся ID-указатель перехватывания. Можете составлять себе словари для них, если очень хочется несколько хуков.
		/// </summary>
		public event KeyActionHandlerEx OnKeyActionEx;
		#endregion

		#region Structing
		/// <summary>
		/// По инициализации перехватчик "подписывается на клавиатурные события" при помощи SetWindowsHookEx.
		/// После этого можно подписаться на дёргаемые им события.
		/// </summary>
		public KeyHooker() {
			Hook();
		}
		/// <summary>
		/// В этом конструкторе можно сказать, должен ли Hooker немедленно начать перехват
		/// </summary>
		/// <param name="immediatelyHook">True - перехват начнётся сразу. Подписывайся и реагируй.
		/// False - можешь подписаться на события и после этого начать перехват методом Hook.</param>
		public KeyHooker(bool immediatelyHook) {
			if (immediatelyHook)
				Hook();
		}
		#endregion

		#region Public Commands
		/// <summary>
		/// Hook them all!
		/// </summary>
		public void Hook() {
			if (_hookID != IntPtr.Zero) return;
			_hookID = SetHook(HookCallback);
		}
		/// <summary>
		/// На случай проблем с устойчивостью перехвата.
		/// Если перехват ещё выполняется, он просто начнётся этим методом.
		/// For the case of instable hook.
		/// </summary>
		public void Rehook() {
			if (_hookID != IntPtr.Zero) {
				UnhookWindowsHookEx(_hookID);
				_hookID = IntPtr.Zero;
			}
			_hookID = SetHook(HookCallback);
		}
		/// <summary>
		/// Let's get out here.
		/// </summary>
		public void Unhook() {
			UnhookWindowsHookEx(_hookID);
			_hookID = IntPtr.Zero;
		}
		#endregion

	}

	public class KeyBinder { //TODO: There is no KeyBinder. ;)
		#region Serving objects
		KeyHooker _hooker;
		#endregion

		#region Structing
		public KeyBinder() {
			_hooker = new KeyHooker();

		}
		#endregion
	}

	static class LayoutWatcher {
		#region  DLL signatures
		// Сохранил полный путь для.
		[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
		private static extern IntPtr GetKeyboardLayout(int windowsThreadProcessID);

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		private static extern int GetWindowThreadProcessId(IntPtr handleWindow, out int processID);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		public static extern IntPtr GetForegroundWindow();
		#endregion

		#region Gettings
		/// <summary>
		/// Получить текущую раскладку
		/// </summary>
		/// <returns>Идентификационный номер активной раскладки</returns>
		public static int GetLayoutID() {
			IntPtr fWin = GetForegroundWindow();
			int a; int winThrProsID = GetWindowThreadProcessId(fWin, out a);
			IntPtr lOut = GetKeyboardLayout(winThrProsID);
			for (int i = 0; i < InputLanguage.InstalledInputLanguages.Count; i++)
				if (lOut == InputLanguage.InstalledInputLanguages[i].Handle)
					return InputLanguage.InstalledInputLanguages[i].Culture.KeyboardLayoutId;
			return 0;
		}
		#endregion
	}

	public class KeyLogger { //TODO: There is no KeyLogger. ;)
		#region Serving objects
		KeyHooker _hooker;
		#endregion

		#region Structing
		public KeyLogger() {
			_hooker = new KeyHooker(false);
			_hooker.OnKeyAction += _hooker_OnKeyAction;
		}
		#endregion

		void _hooker_OnKeyAction(IntPtr hookID, KeyActionArgs e) {
			
		}

	}

}