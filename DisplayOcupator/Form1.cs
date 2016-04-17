using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace DisplayOcupator
{
	public partial class Form1 : Form, INotifyPropertyChanged
	{
		internal class ListBoxItem
		{
			public string SetName { get; set; }
			public string Url { get; set; }

			public override string ToString()
			{
				return SetName;
			}
		}

		private class CardInfo
		{
			public string Name { get; set; }
			public string Price { get; set; }
			public string Rarity { get; set; }
		}

		ConcurrentQueue<CardInfo> cardInfos = new ConcurrentQueue<CardInfo>();

		public string CardList
		{
			get
			{
				return cardInfos.Any() ? 
					cardInfos.Where(c => string.IsNullOrEmpty(comboBox1.Text) || comboBox1.Text == "-" || c.Rarity == comboBox1.Text)
							 .Select(c => c.Name + "(" + c.Rarity + ")" + ": " + c.Price)
							 .Aggregate("", (a, b) => a + Environment.NewLine + b)
					: "";
			}
		}

		public string MainResult
		{
			get
			{
				if (cardInfos.All(i => i.Rarity != "rare") || cardInfos.All(i => i.Rarity != "uncommon") || cardInfos.All(i => i.Rarity != "common"))
					return "";
				
				var rare = cardInfos.Where(i => i.Rarity == "rare").Average(i => Convert(i.Price));
				var uncommon = cardInfos.Where(i => i.Rarity == "uncommon").Average(i => Convert(i.Price));
				var common = cardInfos.Where(i => i.Rarity == "common").Average(i => Convert(i.Price));
				var mythic = cardInfos.Any(i => i.Rarity == "mythic") ? cardInfos.Where(i => i.Rarity == "mythic").Average(i => Convert(i.Price)) : 0m;
				var busters = numericUpDown1.Value;
				return
					string.Format("Mythic: {5:00.00}({6:00.00}){0}Rare: {1:00.00}({7:00.00}){0}Uncommon: {2:00.00}({8:00.00}){0}Common: {3:00.00}({9:00.00}){0}Overall Rare and Mythic: {4:00.00}",
						Environment.NewLine, rare*busters, uncommon*busters*3, common*busters*11, (rare * 0.875m + mythic * 0.125m) * busters, mythic * busters / 8,
						mythic, rare, uncommon, common);
			}
		}

		private decimal Convert(string price)
		{
			if (string.IsNullOrEmpty(price))
				return 0;
			if (price.StartsWith("$"))
				price = price.Substring(1);
			return decimal.Parse(price, CultureInfo.InvariantCulture);
		}

		public Form1()
		{
			InitializeComponent();

			label1.DataBindings.Add("Text", this, "CardList");
			label3.DataBindings.Add("Text", this, "MainResult");
		}

		private async void Form1_Load(object sender, EventArgs e)
		{
			var client = new HttpClient();
			var setsData = await client.GetStringAsync("https://api.deckbrew.com/mtg/sets");
			dynamic result = JsonConvert.DeserializeObject(setsData);
			foreach (dynamic setData in result)
			{
				listBox1.Items.Add(new ListBoxItem() { SetName = setData.name, Url = setData.cards_url });
			}
			client.Dispose();
		}

		private async void listBox1_SelectedIndexChanged(object sender, EventArgs e)
		{
			var listItem = listBox1.SelectedItem as ListBoxItem;
			if (listItem == null)
				return;
			try
			{
				numericUpDown1.Enabled = false;
				await LoadCards(listItem.Url, listItem.SetName);

				OnPropertyChanged("MainResult");
				numericUpDown1.Enabled = true;
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}

		CancellationTokenSource mCts = null;

		private async Task LoadCards(string url, string setName)
		{
			if (mCts != null)
			{
				mCts.Cancel();
			}
			mCts = new CancellationTokenSource();
			var token = mCts.Token;

			cardInfos = new ConcurrentQueue<CardInfo>();
			var cardsClient = new HttpClient();
			var page = 0;
			var found = 1;
			do
			{
				var cardsData = await cardsClient.GetStringAsync(url + "&page=" + page++);

				IEnumerable<dynamic> result = (dynamic)JsonConvert.DeserializeObject(cardsData);
				var tasks = result.Select(c => ((Task<CardInfo>)GetCardData(c, cardsClient, setName, token)).ToObservable());
				found = tasks.Count();
				var obser = tasks.Merge();

				await obser.ForEachAsync(ci =>
				{
					token.ThrowIfCancellationRequested();
					cardInfos.Enqueue(ci);
					Invoke(new Action(() => OnPropertyChanged("CardList")));
				}, token);
			} while (found > 0);
			OnPropertyChanged("CardList");
		}

		private async Task<CardInfo> GetCardData(dynamic card, HttpClient client, string setName, CancellationToken token)
		{
			var info = new CardInfo();
			info.Name = card.name;
			info.Rarity = ((IEnumerable<dynamic>) card.editions).First(e => e.set == setName).rarity;
			var erspString = string.Format("http://magictcgprices.appspot.com/api/cfb/price.json?cardname={0}&setname={1}",
				info.Name.Replace("Æ", "AE"), setName);
			var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, erspString)
						{
							Headers = {{"Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8"}}
						}, token);
			var price = await response.Content.ReadAsStringAsync();

			try
			{
				dynamic priceResult = JsonConvert.DeserializeObject(price);

				info.Price = priceResult[0];
			}
			catch (Exception)
			{
				info.Price = "$-1";
			}
			
			return info;
		}

		#region INPC

		public event PropertyChangedEventHandler PropertyChanged;

		protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			var handler = PropertyChanged;
			if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
		}

		#endregion

		private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
		{
			OnPropertyChanged("CardList");
		}

		private void numericUpDown1_ValueChanged(object sender, EventArgs e)
		{
			OnPropertyChanged("MainResult");
		}
	}
}
