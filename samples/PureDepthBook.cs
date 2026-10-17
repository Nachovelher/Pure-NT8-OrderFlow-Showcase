//
// PureDepthBook - Libro L2 por POSICION (pieza compartida)
//
// NinjaTrader describe la profundidad por posicion, no por precio: Add inserta una fila
// en Position desplazando las de debajo, Remove quita la fila Position subiendo el resto
// y Update reescribe la fila Position. Guardar el libro como diccionario por precio y
// descartar los eventos con Position >= N deja niveles fantasma: una fila que sale de
// las N primeras por una insercion nunca recibe su Remove dentro del filtro y se queda
// congelada con su ultimo tamano. Aqui se replica el libro entero tal como lo manda el
// feed y el filtro "mejores N" se aplica al LEER.
//
// Cada fila guarda su tamano por tramos de antiguedad (cada aumento con su hora) para
// el filtro de persistencia anti-spoofing: Sum(minAge) cuenta solo lo que lleva ese
// tiempo en el libro. Sin dependencias de NinjaTrader: el indicador traduce
// MarketDepthEventArgs a (isAsk, isAdd, isRemove, position, price, volume, time).
// Un consumidor por instancia, desde el hilo de datos del instrumento.
//
// Instalacion: Documents\NinjaTrader 8\bin\Custom\AddOns\ y F5.
//
using System;
using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.AddOns
{
	public class PureDepthRow
	{
		// Tramos de antiguedad por fila; si se superan se funden los dos mas viejos.
		private const int MaxChunks = 16;

		public double	Price;
		public double	Volume;
		public DateTime	Since;		// desde cuando la fila esta en este precio

		// Tamano por antiguedad: cada aumento es un tramo con su propia hora, asi que
		// anadir tamano a un nivel ya asentado NO madura al instante (antes contaba
		// entero en cuanto el nivel llevaba MinLifetime, y el spoof se colaba ahi). Las
		// bajadas consumen primero lo mas nuevo (LIFO): es lo que se retira en un spoof.
		private readonly List<KeyValuePair<DateTime, double>> chunks = new List<KeyValuePair<DateTime, double>>();

		internal void Reset(double price, double volume, DateTime time)
		{
			Price	= price;
			Volume	= volume;
			Since	= time;
			chunks.Clear();
			if (volume > 0)
				chunks.Add(new KeyValuePair<DateTime, double>(time, volume));
		}

		internal void Resize(double volume, DateTime time)
		{
			double delta = volume - Volume;

			if (delta > 0)
			{
				chunks.Add(new KeyValuePair<DateTime, double>(time, delta));
				if (chunks.Count > MaxChunks)
				{
					// Se funden los dos tramos contiguos MAS PROXIMOS en el tiempo, con la hora
					// del mas nuevo: el error de antiguedad queda acotado a ese hueco minimo.
					// Fundir siempre los dos mas viejos rejuvenecia el tamano asentado en cada
					// rafaga y un nivel con aumentos continuos no llegaba a madurar nunca.
					int best = 0;
					for (int i = 1; i < chunks.Count - 1; i++)
						if (chunks[i + 1].Key - chunks[i].Key < chunks[best + 1].Key - chunks[best].Key)
							best = i;
					chunks[best + 1] = new KeyValuePair<DateTime, double>(chunks[best + 1].Key, chunks[best].Value + chunks[best + 1].Value);
					chunks.RemoveAt(best);
				}
			}
			else if (delta < 0)
			{
				double take = -delta;
				for (int i = chunks.Count - 1; i >= 0 && take > 0; i--)
				{
					double amount = chunks[i].Value;
					if (amount <= take)
					{
						take -= amount;
						chunks.RemoveAt(i);
					}
					else
					{
						chunks[i] = new KeyValuePair<DateTime, double>(chunks[i].Key, amount - take);
						take = 0;
					}
				}
			}

			Volume = volume;
		}

		// Tamano que lleva al menos minAgeMs en el libro.
		public double MatureVolume(DateTime now, double minAgeMs)
		{
			if (minAgeMs <= 0)
				return Volume;

			double sum = 0;
			foreach (KeyValuePair<DateTime, double> chunk in chunks)
				if ((now - chunk.Key).TotalMilliseconds >= minAgeMs)
					sum += chunk.Value;
			return sum;
		}
	}

	// Lo que ha cambiado con un evento, para quien detecte apariciones y retiradas.
	public struct PureDepthChange
	{
		public bool		IsAsk;
		public int		Kind;			// 1 alta, 0 actualizacion, -1 baja
		public int		Position;
		public double	Price;
		public double	OldPrice;		// precio que tenia la fila antes del evento (= Price salvo Update que la reutiliza)
		public double	OldVolume;		// tamano que habia en ESTE precio antes del evento
		public double	NewVolume;		// tamano tras el evento (0 en una baja)

		// Baja de la ultima fila con el libro en su tamano maximo: lo normal es que esa
		// fila salga del rango de profundidad del feed, no que se haya cancelado.
		public bool		IsEdgeDrop;
	}

	public class PureDepthBook
	{
		// Techo de filas por lado: un feed que no mande bajas no debe crecer sin fin.
		private const int MaxRows = 200;

		private readonly List<PureDepthRow> asks = new List<PureDepthRow>();
		private readonly List<PureDepthRow> bids = new List<PureDepthRow>();
		private int maxAskRows;
		private int maxBidRows;

		private List<PureDepthRow> Side(bool isAsk) => isAsk ? asks : bids;

		private static bool SamePrice(double a, double b) => Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Abs(a));

		public int Count(bool isAsk) => Side(isAsk).Count;

		public PureDepthRow Row(bool isAsk, int position)
		{
			List<PureDepthRow> side = Side(isAsk);
			return position >= 0 && position < side.Count ? side[position] : null;
		}

		// Aplica un evento de profundidad. False si el evento no encaja con el libro
		// conocido (Update / Remove de una fila que no existe): se ignora sin romper nada.
		public bool Apply(bool isAsk, bool isAdd, bool isRemove, int position, double price, double volume,
						  DateTime time, out PureDepthChange change)
		{
			change = new PureDepthChange { IsAsk = isAsk, Position = position, Price = price, OldPrice = price };
			List<PureDepthRow> side = Side(isAsk);

			if (position < 0)
				return false;

			// Update justo al final del libro conocido: si el feed no mando la foto inicial
			// como altas (o se perdio una), la fila no existe todavia. Se trata como alta
			// para que el libro se rehaga solo en vez de quedarse desincronizado.
			if (!isAdd && !isRemove && position == side.Count)
				isAdd = true;

			if (isAdd)
			{
				// Posicion mas alla del final: el feed se salto filas; se anade al final.
				if (position > side.Count)
					position = side.Count;

				PureDepthRow added = new PureDepthRow();
				added.Reset(price, volume, time);
				side.Insert(position, added);
				if (side.Count > MaxRows)
					side.RemoveAt(side.Count - 1);

				if (isAsk)	maxAskRows = Math.Max(maxAskRows, side.Count);
				else		maxBidRows = Math.Max(maxBidRows, side.Count);

				change.Kind			= 1;
				change.Position		= position;
				change.NewVolume	= volume;
				return true;
			}

			if (position >= side.Count)
				return false;

			PureDepthRow row = side[position];

			if (isRemove)
			{
				change.Kind			= -1;
				change.Price		= row.Price;
				change.OldPrice		= row.Price;
				change.OldVolume	= row.Volume;
				change.IsEdgeDrop	= position == side.Count - 1 && side.Count >= (isAsk ? maxAskRows : maxBidRows);
				side.RemoveAt(position);
				return true;
			}

			// Update. Si el feed reutiliza la fila para OTRO precio, es otro nivel: se
			// reinicia su antiguedad y no hay tamano previo en este precio.
			bool same			= SamePrice(row.Price, price);
			change.Kind			= 0;
			change.OldPrice		= row.Price;
			change.OldVolume	= same ? row.Volume : 0;
			change.NewVolume	= volume;

			if (same)
				row.Resize(volume, time);
			else
				row.Reset(price, volume, time);
			return true;
		}

		// Tamano sumado de las mejores n filas de un lado. Con minAgeMs > 0 solo cuenta el
		// tamano que lleva al menos ese tiempo en el libro (por tramos, ver PureDepthRow).
		public double Sum(bool isAsk, int n, DateTime now, double minAgeMs)
		{
			List<PureDepthRow> side = Side(isAsk);
			int last = Math.Min(n, side.Count);
			double sum = 0;

			for (int i = 0; i < last; i++)
				sum += side[i].MatureVolume(now, minAgeMs);

			return sum;
		}

		// Mayor tamano entre las mejores n filas de un lado.
		public double Largest(bool isAsk, int n)
		{
			List<PureDepthRow> side = Side(isAsk);
			int last = Math.Min(n, side.Count);
			double max = 0;

			for (int i = 0; i < last; i++)
				if (side[i].Volume > max)
					max = side[i].Volume;

			return max;
		}

		public void Clear()
		{
			asks.Clear();
			bids.Clear();
			maxAskRows = 0;
			maxBidRows = 0;
		}
	}
}
