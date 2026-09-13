using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IES_2.Avalonia.Models;
using IES_2.Avalonia.Services;
using IES_2.ECU;

namespace IES_2.Avalonia.ViewModels
{
    /// <summary>
    /// [F2] Parameters screen: live engine data, replaces frmMain's dgvParameters +
    /// checkbox-driven Request[] wiring and RefreshData().
    /// </summary>
    public partial class ParametersViewModel : ObservableObject
    {
        private readonly DiagnosticsSession _session;

        public ObservableCollection<ParameterRow> Rows { get; } = new();

        /// <summary>Raised whenever a row's checked state changes, so the Graphs screen can keep its trace list in sync.</summary>
        public event Action<ParameterRow>? RowCheckedChanged;

        public ParametersViewModel(DiagnosticsSession session)
        {
            _session = session;
            foreach (var data in session.Ecu.engineData)
            {
                var row = new ParameterRow(data);
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ParameterRow.IsChecked))
                        OnRowCheckedChanged(row);
                };
                Rows.Add(row);
            }
            session.ParametersUpdated += OnParametersUpdated;
        }

        private void OnRowCheckedChanged(ParameterRow row)
        {
            foreach (var idx in row.RequestSet)
                _session.RequestEnabled[idx] = row.IsChecked;
            if (!row.IsChecked)
                row.Value = "";
            RowCheckedChanged?.Invoke(row);
        }

        [RelayCommand]
        private void CheckAll()
        {
            foreach (var row in Rows)
                row.IsChecked = true;
        }

        [RelayCommand]
        private void UncheckAll()
        {
            foreach (var row in Rows)
                row.IsChecked = false;
        }

        private void OnParametersUpdated() => Dispatcher.UIThread.Post(RefreshValues);

        private void RefreshValues()
        {
            foreach (var row in Rows)
            {
                if (!row.IsChecked) continue;
                row.Value = ecu.Valid[row.RequestSet[0]] ? row.Data.FormattedValue : "#ERR";
            }
        }

        public void Detach() => _session.ParametersUpdated -= OnParametersUpdated;
    }
}
