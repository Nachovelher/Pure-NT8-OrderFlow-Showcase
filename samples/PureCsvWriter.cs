//
// PureCsvWriter - Escritura de CSV en segundo plano y en orden (pieza compartida)
//
// Las herramientas Pure que exportan datos (marcador de senales, PureL1Flow) no deben
// escribir a disco desde el hilo de datos: la carpeta de NinjaTrader esta en OneDrive y
// un fichero bloqueado durante la sincronizacion congelaria el feed. Cada linea se
// encadena como tarea: se escriben en orden y fuera del hilo de datos. La cabecera se
// escribe solo si el fichero no existia. El primer error queda en LastError para que el
// indicador lo avise una vez en el log.
//
// Numeros con cultura invariante (punto decimal) y coma como separador.
//
// Instalacion: Documents/NinjaTrader 8/bin/Custom/AddOns/ y F5.
//
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace NinjaTrader.NinjaScript.AddOns
{
	public class PureCsvWriter
	{
		// Un cerrojo por ruta: dos instancias con el mismo fichero (dos charts iguales) no
		// duplican la cabecera ni chocan al abrirlo a la vez (la segunda apertura fallaria).
		private static readonly ConcurrentDictionary<string, object> pathLocks =
			new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

		private readonly string		filePath;
		private readonly string		header;
		private readonly object		fileSync;
		private readonly object		chainSync = new object();
		private Task				chain = Task.FromResult(0);
		private bool				headerChecked;		// solo se toca dentro de la cadena (secuencial)

		public volatile string LastError;

		public PureCsvWriter(string filePath, string header)
		{
			this.filePath	= filePath;
			this.header		= header;
			fileSync		= pathLocks.GetOrAdd(filePath ?? string.Empty, _ => new object());
		}

		public string FilePath => filePath;

		public void Append(string line)
		{
			lock (chainSync)
				chain = chain.ContinueWith(previous => Write(line), TaskScheduler.Default);
		}

		// Espera a que se escriba lo encolado (tests y cierre ordenado). False si vence el plazo.
		public bool Flush(int timeoutMs)
		{
			Task pending;
			lock (chainSync)
				pending = chain;
			return pending.Wait(timeoutMs);
		}

		private void Write(string line)
		{
			try
			{
				lock (fileSync)
				{
					if (!headerChecked)
					{
						headerChecked = true;
						string dir = Path.GetDirectoryName(filePath);
						if (!string.IsNullOrEmpty(dir))
							Directory.CreateDirectory(dir);
						if (!File.Exists(filePath) && !string.IsNullOrEmpty(header))
							File.AppendAllText(filePath, header + Environment.NewLine);
					}
					File.AppendAllText(filePath, line + Environment.NewLine);
				}
			}
			catch (Exception ex)
			{
				if (LastError == null)
					LastError = ex.Message;
			}
		}

		public static string Num(double v)
		{
			return double.IsNaN(v) || double.IsInfinity(v) ? string.Empty : v.ToString("0.######", CultureInfo.InvariantCulture);
		}

		public static string Stamp(DateTime t)
		{
			return t.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
		}

		// Nombre de fichero valido ("MNQ 12-26" -> "MNQ_12-26").
		public static string SafeName(string s)
		{
			if (string.IsNullOrEmpty(s))
				return "sin_nombre";

			char[] invalid = Path.GetInvalidFileNameChars();
			StringBuilder sb = new StringBuilder(s.Length);
			foreach (char c in s)
				sb.Append(c == ' ' || Array.IndexOf(invalid, c) >= 0 ? '_' : c);
			return sb.ToString();
		}
	}
}
