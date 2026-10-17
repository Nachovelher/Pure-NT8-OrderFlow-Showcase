//
// PureTimeCalibration - Calibracion horaria de umbrales
//
// La actividad tipica no es la misma en la apertura que a mediodia: un umbral fijo
// dispara de mas en la apertura y de menos en horas lentas. Esta clase aprende la
// media de una metrica por bloque de 30 minutos del dia y devuelve un factor de
// escala (bucket / media global) acotado, o el valor esperado del bloque.
//
// La usan PureTapeSpeed, PureSweepDetector y PureEffortResult. En memoria de la
// instancia: aprende durante la sesion (y del historico si el indicador lo procesa).
//
// Instalacion: Documents\NinjaTrader 8\bin\Custom\AddOns\ y F5.
//
using System;

namespace NinjaTrader.NinjaScript.AddOns
{
	public class PureTimeCalibration
	{
		private const int Buckets = 48;			// bloques de 30 min
		private const int MinSamples = 30;		// muestras minimas para fiarse de un bloque

		private readonly double[]	avg	= new double[Buckets];
		private readonly int[]		cnt	= new int[Buckets];
		private double				globalAvg;
		private int					globalCnt;

		private static int Bucket(DateTime t) => Math.Min(Buckets - 1, (t.Hour * 60 + t.Minute) / 30);

		public void Update(DateTime time, double value)
		{
			if (value <= 0)
				return;

			int b = Bucket(time);
			cnt[b]++;
			avg[b] += (value - avg[b]) / Math.Min(cnt[b], 500);

			globalCnt++;
			globalAvg += (value - globalAvg) / Math.Min(globalCnt, 5000);
		}

		// Factor de escala del bloque actual respecto a la media global, acotado [0.4, 3].
		// 1 si aun no hay muestra suficiente.
		public double Factor(DateTime time)
		{
			int b = Bucket(time);
			if (cnt[b] < MinSamples || globalAvg <= 0)
				return 1;
			return Math.Max(0.4, Math.Min(3, avg[b] / globalAvg));
		}

		// Valor esperado de la metrica en el bloque actual; fallback si no hay muestra.
		public double Expected(DateTime time, double fallback)
		{
			int b = Bucket(time);
			return cnt[b] >= MinSamples ? avg[b] : fallback;
		}
	}
}
