using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace DisplayOcupator
{
	public partial class Form1 : Form, INotifyPropertyChanged
	{
		private class ListBoxItem
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
		}

		List<CardInfo> cardInfos = new List<CardInfo>();

		public string CardList
		{
			get { return cardInfos.Any() ? cardInfos.Select(c => c.Name + ": " + c.Price).Aggregate((a, b) => a + Environment.NewLine + b) : ""; }
		}

		public Form1()
		{
			InitializeComponent();

			label1.DataBindings.Add("Text", this, "CardList");
		}

		private void Form1_Load(object sender, EventArgs e)
		{
			var client = new WebClient();
			client.Encoding = new UTF8Encoding();
			var listBoxItems = new List<ListBoxItem>();
			var setsData = client.DownloadString("https://api.deckbrew.com/mtg/sets");
			dynamic result = JsonConvert.DeserializeObject(setsData);
			foreach (dynamic setData in result)
			{
				listBoxItems.Add(new ListBoxItem() { SetName = setData.name, Url = setData.cards_url });
			}
			listBox1.Items.AddRange(listBoxItems.ToArray());
			client.Dispose();
		}

		private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
		{
			var listItem = listBox1.SelectedItem as ListBoxItem;
			if (listItem == null)
				return;
			Text = listItem.Url;
			LoadCards(listItem.Url, listItem.SetName);
		}

		private void LoadCards(string url, string setName)
		{
			cardInfos.Clear();
			var cardsClient = new WebClient();
			cardsClient.DownloadStringCompleted += CardsClientOnDownloadStringCompleted;
			cardsClient.DownloadStringAsync(new Uri(url), new { page = 0, url, setName });
			OnPropertyChanged("CardList");
		}

		private void CardsClientOnDownloadStringCompleted(object sender, DownloadStringCompletedEventArgs e)
		{
			dynamic result = JsonConvert.DeserializeObject(e.Result);
			int found = 0;
			dynamic userInfo = e.UserState;
			foreach (var cardData in result)
			{
				var info = new CardInfo();
				info.Name = cardData.name;
				var priceClient = new WebClient();
				priceClient.DownloadStringAsync(new Uri(string.Format("http://magictcgprices.appspot.com/api/cfb/price.json?cardname={0}&setname={1}", info.Name.Replace("Æ", "AE"), userInfo.setName)), info);
				priceClient.DownloadStringCompleted += client_DownloadStringCompleted;
				cardInfos.Add(info);
				found++;
			}

			OnPropertyChanged("CardList");
			var client = (sender as WebClient);
			if (found > 0)
			{
				client.DownloadStringAsync(new Uri(userInfo.url + "&page=" + userInfo.page + 1), new { page = userInfo.page + 1, userInfo.url, userInfo.setName });
				return;
			}

			client.Dispose();
		}

		void client_DownloadStringCompleted(object sender, DownloadStringCompletedEventArgs e)
		{
			var cardInfo = e.UserState as CardInfo;
			dynamic result = JsonConvert.DeserializeObject(e.Result);
			cardInfo.Price = result[0];

			OnPropertyChanged("CardList");

			(sender as WebClient).Dispose();
		}

		#region INPC

		public event PropertyChangedEventHandler PropertyChanged;

		protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			var handler = PropertyChanged;
			if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
		}

		#endregion

	}
}
