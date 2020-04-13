﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CoreLocation;
using Covid19Radar.Common;
using Covid19Radar.Model;
using Covid19Radar.Services;
using Foundation;
using CoreBluetooth;
using UIKit;
using Xamarin.Forms;
using SQLite;

[assembly: Dependency(typeof(Covid19Radar.iOS.Services.BeaconService))]
namespace Covid19Radar.iOS.Services
{
    public class BeaconService : IBeaconService
    {
        private UserDataModel _userData;
        private bool _transmitterFlg = false;
        private CBPeripheralManager _beaconTransmitter = new CBPeripheralManager();
        private CLBeaconRegion _fieldRegion;
        private CLLocationManager _beaconManager;
        private List<CLBeaconRegion> _listOfCLBeaconRegion;
        private readonly SQLiteConnection _connection;
        public BeaconService()
        {
            _connection = AppDelegate.sqliteConnectionProvider.GetConnection();
            _connection.CreateTable<BeaconDataModel>();

            _userData = new UserDataModel();
            _beaconManager = new CLLocationManager();
            _beaconTransmitter = new CBPeripheralManager();
            _beaconTransmitter.AdvertisingStarted += DidAdvertisingStarted;
            _beaconTransmitter.StateUpdated += DidStateUpdated;

            _listOfCLBeaconRegion = new List<CLBeaconRegion>();
            _fieldRegion = new CLBeaconRegion(new NSUuid(AppConstants.AppUUID), "");
            _fieldRegion.NotifyEntryStateOnDisplay = true;
            _fieldRegion.NotifyOnEntry = true;
            _fieldRegion.NotifyOnExit = true;
        }

        public CLLocationManager BeaconManagerImpl
        {
            get
            {
                if (_beaconManager == null)
                {
                    _beaconManager = InitializeBeaconManager();
                }
                return _beaconManager;
            }
        }

        public void InitializeService()
        {
            _beaconManager = InitializeBeaconManager();
            StartBeacon();

            UserDataModel userDataModel = new UserDataModel();
            userDataModel.UserUuid = Guid.NewGuid().ToString();
            userDataModel.Major = "24";
            userDataModel.Minor = "51";
            StartAdvertising(userDataModel);
        }

        private CLLocationManager InitializeBeaconManager()
        {
            // Enable the BeaconManager 
            _beaconManager = new CLLocationManager();

            // BeaconManager Setting

            // Monitoring
            _beaconManager.DidDetermineState += DetermineStateForRegionComplete;
            _beaconManager.RegionEntered += EnterRegionComplete;
            _beaconManager.RegionLeft += ExitRegionComplete;


            _beaconManager.RequestAlwaysAuthorization();
            _beaconManager.DidRangeBeacons += DidRangeBeconsInRegionComplete;

            _beaconManager.AuthorizationChanged += HandleAuthorizationChanged;
            return _beaconManager;
        }

        public List<BeaconDataModel> GetBeaconData()
        {
            return _connection.Table<BeaconDataModel>().ToList();
        }

        public void StartBeacon()
        {
            System.Diagnostics.Debug.WriteLine("StartBeacon");

            _beaconManager.RequestAlwaysAuthorization();
            _beaconManager.PausesLocationUpdatesAutomatically = false;

            _listOfCLBeaconRegion.Add(_fieldRegion);
            _beaconManager.StartMonitoring(_fieldRegion);
        }

        public void StopBeacon()
        {
            System.Diagnostics.Debug.WriteLine("StopBeacon");

            _beaconManager.StopRangingBeacons(_fieldRegion);
            _beaconManager.StopMonitoring(_fieldRegion);
        }

        public void StartAdvertising(UserDataModel userData)
        {
            _userData = userData;
            _transmitterFlg = true;

            /*
                        CLBeaconRegion region = new CLBeaconRegion(new NSUuid(AppConstants.AppUUID), ushort.Parse(userData.Major), ushort.Parse(userData.Minor), userData.UserUuid);
                        NSNumber txPower = new NSNumber(-59);
                        NSDictionary peripheralData = region.GetPeripheralData(txPower);
                        */

        }

        private void DidAdvertisingStarted(object sender, NSErrorEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("DidAdvertisingStarted");

        }

        private void DidStateUpdated(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("DidStateUpdated");
            CBPeripheralManager trasmitter = sender as CBPeripheralManager;

            if (trasmitter.State < CBPeripheralManagerState.PoweredOn)
            {
                System.Diagnostics.Debug.WriteLine("Bluetooth must be enabled");
                //                new UIAlertView("Bluetooth must be enabled", "To configure your device as a beacon", null, "OK", null).Show();
                return;
            }

            if (_transmitterFlg)
            {
                CLBeaconRegion region = new CLBeaconRegion(new NSUuid(AppConstants.AppUUID), ushort.Parse(_userData.Major), ushort.Parse(_userData.Minor), _userData.UserUuid);
                NSNumber txPower = new NSNumber(-59);
                trasmitter.StartAdvertising(region.GetPeripheralData(txPower));
            }
            else
            {
                trasmitter.StopAdvertising();
            }
        }

        public void StopAdvertising()
        {
            _transmitterFlg = false;
            //            _beaconTransmitter.StopAdvertising();
        }

        private void DidRangeBeconsInRegionComplete(object sender, CLRegionBeaconsRangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("HandleDidDetermineState");

            foreach (var beacon in e.Beacons)
            {
                if (beacon.Proximity == CLProximity.Unknown)
                {
                    return;
                }

                var key = beacon.Uuid.ToString() + beacon.Major.ToString() + beacon.Minor.ToString();
                var result = _connection.Table<BeaconDataModel>().SingleOrDefault(x => x.Id == key);
                if (result == null)
                {
                    BeaconDataModel data = new BeaconDataModel();
                    data.Id = key;
                    data.Count = 0;
                    data.BeaconUuid = beacon.Uuid.ToString();
                    data.Major = beacon.Major.ToString();
                    data.Minor = beacon.Minor.ToString();
                    data.Distance = beacon.Accuracy;
                    data.Rssi = (short)beacon.Rssi;
                    //                        data.TXPower = beacon.tr;
                    data.ElaspedTime = new TimeSpan();
                    data.LastDetectTime = DateTime.Now;
                    _connection.Insert(data);
                }
                else
                {
                    BeaconDataModel data = result;
                    data.Id = key;
                    data.Count++;
                    data.BeaconUuid = beacon.Uuid.ToString();
                    data.Major = beacon.Major.ToString();
                    data.Minor = beacon.Minor.ToString();
                    data.Distance += (beacon.Accuracy - data.Distance) / data.Count;
                    data.Rssi = (short)beacon.Rssi;
                    //                        data.TXPower = beacon.tr;
                    data.ElaspedTime = new TimeSpan();
                    data.LastDetectTime = DateTime.Now;
                    _connection.Update(data);
                    System.Diagnostics.Debug.WriteLine(data.Distance);
                    System.Diagnostics.Debug.WriteLine(data.ElaspedTime.TotalSeconds);
                }

            }

        }

        #region beacon monitoring
        private void DetermineStateForRegionComplete(object sender, CLRegionStateDeterminedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("DetermineStateForRegionComplete");
            System.Diagnostics.Debug.WriteLine(e.ToString());
            _beaconManager.StartRangingBeacons(_fieldRegion);
        }

        private void EnterRegionComplete(object sender, CLRegionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("EnterRegionComplete ---- StartRanging");
            System.Diagnostics.Debug.WriteLine(e.ToString());
            _beaconManager.StartRangingBeacons(_fieldRegion);

        }

        private void ExitRegionComplete(object sender, CLRegionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ExitRegionComplete ---- StopRanging");
            System.Diagnostics.Debug.WriteLine(e.ToString());
            _beaconManager.StopRangingBeacons(_fieldRegion);

        }

        #endregion
        #region Auth

        private void HandleAuthorizationChanged(object sender, CLAuthorizationChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("HandleAuthorizationChanged");
            // Do That Stop beacons
        }

        #endregion
    }
}
