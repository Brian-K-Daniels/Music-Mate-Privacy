using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using Microsoft.Maui.Storage;

namespace musicmate.ViewModels
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        const string KeyCollectNote = "CollectNoteStats";
        const string KeyCollectSession = "CollectSessionStats";
        const string KeyMaxNoteDbMb = "MaxNoteDbSizeMb";

        public SettingsViewModel()
        {
            _collectNoteStats = Preferences.Default.Get(KeyCollectNote, true);
            _collectSessionStats = Preferences.Default.Get(KeyCollectSession, true);
            _maxNoteDbSizeMb = Preferences.Default.Get(KeyMaxNoteDbMb, 50);
        }

        // Lightweight replacement for CommunityToolkit.Mvvm's ObservableObject.SetProperty
        protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(backingStore, value))
                return false;

            backingStore = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "") =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));


        bool _collectNoteStats;
        public bool CollectNoteStats
        {
            get => _collectNoteStats;
            set
            {
                if (SetProperty(ref _collectNoteStats, value))
                {
                    Preferences.Default.Set(KeyCollectNote, value);
                }
            }
        }

        bool _collectSessionStats;
        public bool CollectSessionStats
        {
            get => _collectSessionStats;
            set
            {
                if (SetProperty(ref _collectSessionStats, value))
                {
                    Preferences.Default.Set(KeyCollectSession, value);
                }
            }
        }

        double _maxNoteDbSizeMb;
        public double MaxNoteDbSizeMb
        {
            get => _maxNoteDbSizeMb;
            set
            {
                if (SetProperty(ref _maxNoteDbSizeMb, value))
                {
                    Preferences.Default.Set(KeyMaxNoteDbMb, (int)value);
                    OnPropertyChanged(nameof(MaxNoteDbSizeDisplay));
                }
            }
        }

        public string MaxNoteDbSizeDisplay => $"Max Note DB size: {(int)MaxNoteDbSizeMb} MB";
    }
}
