﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows.Shell;
using System.Windows.Threading;
using Grabacr07.KanColleViewer.Composition;
using Grabacr07.KanColleViewer.Plugins.Properties;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Linq;
using MetroTrilithon.Mvvm;
using StatefulModel;

namespace Grabacr07.KanColleViewer.Plugins
{
	[Export(typeof(IPlugin))]
	[Export(typeof(ITaskbarProgress))]
	[ExportMetadata("Guid", guid)]
	[ExportMetadata("Title", "HpProgressIndicator")]
	[ExportMetadata("Description", "艦隊内の最大損害艦耐久をタスク バー インジケーターに報告します。")]
	[ExportMetadata("Version", "1.0")]
	[ExportMetadata("Author", "@veigr")]
	public class HpProgress : IPlugin, ITaskbarProgress, IDisposableHolder
	{
		private const string guid = "DA0E7091-F4A6-4467-9812-3C3E0DF946EA";

		private readonly MultipleDisposable compositDisposable = new MultipleDisposable();
		private List<IDisposable> fleetHandlers = new List<IDisposable>();
		private bool initialized;

		public string Id => guid + "-1";

		public string DisplayName => "艦隊内の最大損害艦耐久";

		public TaskbarItemProgressState State { get; private set; }

		public double Value { get; private set; }

		public event EventHandler Updated;

		public void Initialize()
		{
			KanColleClient.Current
				.Subscribe(nameof(KanColleClient.IsStarted), () => this.InitializeCore(), false)
				.AddTo(this);
		}

		private void InitializeCore()
		{
			if (this.initialized) return;

			var homeport = KanColleClient.Current.Homeport;
			if (homeport != null)
			{
				this.initialized = true;
				homeport.Organization
					.Subscribe(nameof(Organization.Fleets), this.UpdateFleets)
					.AddTo(this);
			}
		}

		public void UpdateFleets()
		{
			if (!this.initialized) return;

			foreach (var handler in fleetHandlers)
			{
				handler.Dispose();
			}
			fleetHandlers.Clear();

			KanColleClient.Current.Homeport.Organization.Fleets.Values
				.Select(x => x.Subscribe(nameof(Fleet.Ships), this.Update))
				.Do(x => fleetHandlers.Add(x))
				.ToArray();
			KanColleClient.Current.Homeport.Organization.Fleets.Values
				.Select(x => x.Subscribe(nameof(Fleet.IsInSortie), this.Update))
				.Do(x => fleetHandlers.Add(x))
				.ToArray();
		}

		public void Update()
		{
			var org = KanColleClient.Current.Homeport.Organization;
			IEnumerable<Ship> ships;
			if (org.Fleets.Values.Any(x => x.IsInSortie))
			{
				ships = org.Fleets.Values
					.Where(x => x.IsInSortie)
					.SelectMany(x => x.Ships);
			}
			else
			{
				ships = org.Combined
					? org.CombinedFleet.Fleets.SelectMany(x => x.Ships)
					: org.Fleets[1].Ships;
			}

			if (!ships.Any())
			{
				this.Value = .0;
				this.State = TaskbarItemProgressState.None;
				this.Updated?.Invoke(this, EventArgs.Empty);
				return;
			}

			this.Value = ships.Select(x => (x.HP.Maximum == 0 ? 0.0 : x.HP.Current / (double)x.HP.Maximum)).Min();

			// 0.25 以下のとき、「大破」
			if (this.Value <= 0.25)
			{
				this.State = TaskbarItemProgressState.Error;
				this.Value = 1.0;
			}
			// 0.5 以下のとき、「中破」
			else if (this.Value <= 0.5)
			{
				this.State = TaskbarItemProgressState.Paused;
			}
			else
			{
				this.State = TaskbarItemProgressState.Normal;
			}

			this.Updated?.Invoke(this, EventArgs.Empty);
		}

		public void Dispose() => this.compositDisposable.Dispose();
		ICollection<IDisposable> IDisposableHolder.CompositeDisposable => this.compositDisposable;
	}
}
