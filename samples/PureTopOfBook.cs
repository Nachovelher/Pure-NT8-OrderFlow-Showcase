//
// PureTopOfBook - Microestructura del mejor nivel (pieza compartida, sin NinjaTrader)
//
// Tres medidas del Level 1 (mejor bid y mejor ask con su tamano) que no calcula ningun
// otro indicador Pure ni columna SuperDOM:
//
//   OFI - Order Flow Imbalance (Cont, Kukanov & Stoikov 2014). Flujo neto en el mejor
//         nivel a partir de los CAMBIOS de cotizacion: tamano que entra en el bid o sale
//         del ask suma; tamano que entra en el ask o sale del bid resta. Ve tambien las
//         cancelaciones y las mejoras de precio, no solo las operaciones (que es lo unico
//         que ve el delta). Explica la mayor parte del movimiento de precio simultaneo.
//
//   Microprecio (Stoikov 2018). Precio justo ponderado por las colas del mejor nivel:
//         (Qb * Ask + Qa * Bid) / (Qb + Qa). Con mucho mas tamano en el bid se acerca al
//         ask: el siguiente tick tiende a ir hacia ahi.
//
//   Desequilibrio de cola: (Qb - Qa) / (Qb + Qa), de -1 a +1.
//
// Se alimenta evento a evento con OnQuote(lado, precio, tamano). Sin dependencias de
// NinjaTrader para poder probarlo aislado. Un consumidor por instancia, hilo de datos.
//
// Instalacion: Documents/NinjaTrader 8/bin/Custom/AddOns/ y F5.
//
using System;

namespace NinjaTrader.NinjaScript.AddOns
{
	public class PureTopOfBook
	{
		public double BidPrice	{ get; private set; }
		public double BidSize	{ get; private set; }
		public double AskPrice	{ get; private set; }
		public double AskSize	{ get; private set; }

		public bool HasQuote	=> BidPrice > 0 && AskPrice > 0 && BidSize + AskSize > 0;
		public double Mid		=> HasQuote ? (BidPrice + AskPrice) / 2 : 0;
		public double Spread	=> HasQuote ? AskPrice - BidPrice : 0;

		// Tamano medio de las dos colas: escala natural para normalizar el OFI.
		public double TopDepth	=> (BidSize + AskSize) / 2;

		public double Microprice		=> HasQuote ? (BidSize * AskPrice + AskSize * BidPrice) / (BidSize + AskSize) : 0;
		public double QueueImbalance	=> HasQuote ? (BidSize - AskSize) / (BidSize + AskSize) : 0;

		// Aplica la nueva cotizacion de UN lado y devuelve el incremento de OFI en contratos.
		// Cada evento cambia un solo lado, asi que la formula de Cont et al. se reparte:
		//   bid: + tamano nuevo si el precio sube o se mantiene; - tamano viejo si baja o se mantiene
		//   ask: - tamano nuevo si el precio baja o se mantiene; + tamano viejo si sube o se mantiene
		// Mismo precio = diferencia de tamanos. La primera cotizacion de cada lado solo inicializa.
		public double OnQuote(bool isAsk, double price, double size)
		{
			if (price <= 0 || size < 0)
				return 0;

			double e = 0;
			if (isAsk)
			{
				if (AskPrice > 0)
				{
					if (price <= AskPrice) e -= size;
					if (price >= AskPrice) e += AskSize;
				}
				AskPrice	= price;
				AskSize		= size;
			}
			else
			{
				if (BidPrice > 0)
				{
					if (price >= BidPrice) e += size;
					if (price <= BidPrice) e -= BidSize;
				}
				BidPrice	= price;
				BidSize		= size;
			}
			return e;
		}

		public void Reset()
		{
			BidPrice	= 0;
			BidSize		= 0;
			AskPrice	= 0;
			AskSize		= 0;
		}
	}
}
