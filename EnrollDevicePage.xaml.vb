﻿Imports Dexcom
Imports System.Threading

''' <summary>
''' Author: Jay Lagorio
''' Date: May 15, 2016
''' Summary: Searches for new and unenrolled devices so the user can synchronize them with Nightscout.
''' </summary>

Public NotInheritable Class EnrollDevicePage
    Inherits ContentDialog

    ' The timer that looks for devices while the window is visible
    Private pTimer As Timer

    ' A list of devices that are already in the display to prevent duplicates
    ' and to keep from having to clear/repopulate the list while the user looks
    ' at the devices
    Private pDisplayedConnections As New Collection(Of String)

    ' Constants and labels for devices shown to the user
    Private Const ManufacturerDexcom As String = "Dexcom"
    Private Const ManufacturerMedtronic As String = "Medtronic"
    Private Const ModelShareReceiver As String = "Share Receiver"
    Private Const NoDevicesFoundLabel As String = "(No new devices found)"

    ''' <summary>
    ''' Fires when the dialog is loaded.
    ''' </summary>
    Private Sub ContentDialog_Loaded(sender As Object, e As RoutedEventArgs)
        Call StartTimer()
    End Sub

    ''' <summary>
    ''' Fires when the user clicks the OK button
    ''' </summary>
    Private Async Sub ContentDialog_PrimaryButtonClick(sender As ContentDialog, args As ContentDialogButtonClickEventArgs)
        ' Check to see that a device type is selected
        If lstDeviceType.SelectedIndex >= 0 Then
            ' Check to see that a device is selected
            If lstConnectedDevices.SelectedIndex >= 0 Then
                ' Check to make sure the selected device isn't the No Devices Found label
                If lstConnectedDevices.Items(0).Content <> NoDevicesFoundLabel Then
                    ' Split by manufacturer, interface, possibly DeviceId
                    Dim DeviceAttributes() As String = CStr(lstConnectedDevices.SelectedItem.Tag).Split(":")
                    If DeviceAttributes(0) = ManufacturerDexcom Then ' Dexcom Receiver
                        Select Case DeviceAttributes(1)
                            Case USBInterface.InterfaceName ' USB connection
                                ' Check all Dexcom devices connected via USB
                                Dim Connections As Collection(Of Dexcom.DeviceInterface.DeviceConnection) = Await (New USBInterface).GetAvailableDevices()
                                For i = 0 To Connections.Count - 1
                                    If Connections(i).DeviceId = DeviceAttributes(2) Then
                                        ' Attempt to connect to and enroll the device
                                        If Not Await EnrollDevice(DeviceAttributes(0), USBInterface.InterfaceName, Connections(i)) Then
                                            Await (New Windows.UI.Popups.MessageDialog("An error has occurred.")).ShowAsync
                                            args.Cancel = True
                                        End If

                                    End If
                                Next
                        End Select
                    End If
                End If
            End If
        End If
    End Sub

    ''' <summary>
    ''' Start the timer that scans for new hardware attached to the system
    ''' </summary>
    Private Sub StartTimer()
        ' Starts a timer with an immediate run and a repeat every one second
        pTimer = New Timer(AddressOf TimerProc, Nothing, 0, 1000)
    End Sub

    ''' <summary>
    ''' Stops the hardware scanning timer
    ''' </summary>
    Private Sub StopTimer()
        pTimer = Nothing
    End Sub

    ''' <summary>
    ''' Fires when the user clicks the Cancel button.
    ''' </summary>
    Private Sub ContentDialog_SecondaryButtonClick(sender As ContentDialog, args As ContentDialogButtonClickEventArgs)
        Me.Hide()
    End Sub

    ''' <summary>
    ''' Shows a welcome message to the user.
    ''' </summary>
    Public Sub ShowWelcomeMode()
        lblWelcomeText.Visibility = Visibility.Visible
    End Sub

    ''' <summary>
    ''' Clears the list of devices in the display when the 
    ''' user changes the type of device to look for
    ''' </summary>
    Private Sub lstDeviceType_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles lstDeviceType.SelectionChanged
        Call pDisplayedConnections.Clear()
    End Sub

    ''' <summary>
    ''' Fires when the user selects a device to add
    ''' </summary>
    Private Sub lstConnectedDevices_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles lstConnectedDevices.SelectionChanged
        ' Check to see if a device is selected
        If lstConnectedDevices.SelectedIndex >= 0 Then
            ' Make sure anything selected isn't the No Devices Found message
            If lstConnectedDevices.SelectedValue.Content <> NoDevicesFoundLabel Then

                ' Check the selected device for manufacturer and interface
                Dim DeviceAttributes() As String = CStr(lstConnectedDevices.SelectedItem.Tag).Split(":")
                If DeviceAttributes(0) = ManufacturerDexcom Then ' Dexcom Receiver
                    Select Case DeviceAttributes(1)
                        Case "USB"
                            ' Hide the serial number box, it isn't needed for USB connections to Dexcom Receivers
                            pnlSerialNumber.Visibility = Visibility.Collapsed
                    End Select
                End If
            End If
        End If
    End Sub

    ''' <summary>
    ''' Runs the timer function on the UI thread
    ''' </summary>
    Private Async Sub TimerProc()
        Await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, AddressOf TimerProcUIThread)
    End Sub

    ''' <summary>
    ''' Fires each second looking for a device to let the user enroll
    ''' </summary>
    Private Async Sub TimerProcUIThread()
        Dim DevicesFound As Integer = 0

        If lstDeviceType.SelectedIndex = 0 Then ' Dexcom Receiver 
            ' Check for Dexcom devices on each interface type
            Dim Interfaces() As DeviceInterface = {New USBInterface}
            For i = 0 To Interfaces.Length - 1
                ' Get the number of available devices to list
                Dim Connections As Collection(Of Dexcom.DeviceInterface.DeviceConnection) = Await Interfaces(i).GetAvailableDevices()
                For j = 0 To Connections.Count - 1

                    ' Check to make sure the device isn't already enrolled by comparing the DeviceId or Serial Number
                    Dim DeviceEnrolled As Boolean = False
                    If Settings.EnrolledDevices.Count > 0 Then
                        For k = 0 To Settings.EnrolledDevices.Count - 1
                            Try
                                ' Compare the DeviceId
                                If Connections(j).DeviceId <> "" And Settings.EnrolledDevices(k).DeviceId <> "" Then
                                    If Connections(j).DeviceId = Settings.EnrolledDevices(k).DeviceId Then
                                        DeviceEnrolled = True
                                        Exit For
                                    End If
                                ElseIf Settings.EnrolledDevices(k).SerialNumber <> "" Then
                                    ' Compare the Serial Numbers by first connecting to the potentially new receiver and the existing receivers
                                    Dim PotentialReceiver As New DexcomDevice(Connections(j).InterfaceName, "", Connections(j).DeviceId)
                                    If Await PotentialReceiver.Connect() Then
                                        If Await Settings.EnrolledDevices(k).Connect() Then
                                            If PotentialReceiver.SerialNumber = Settings.EnrolledDevices(k).SerialNumber Then
                                                DeviceEnrolled = True
                                            End If

                                            ' Disconnect after comparisons
                                            Await Settings.EnrolledDevices(k).Disconnect
                                        End If

                                        ' Disconnect the potentially new device
                                        Await PotentialReceiver.Disconnect
                                        If DeviceEnrolled Then
                                            Exit For
                                        End If
                                    End If
                                End If
                            Catch Ex As Exception
                                Continue For
                            End Try
                        Next
                    End If

                    ' If the device isn't already enrolled check to make sure it isn't already displayed
                    If Not DeviceEnrolled Then
                        ' Check to see whether the device is already in the list of connections already displayed
                        If Not pDisplayedConnections.Contains(Connections(j).DeviceId) Then
                            ' Add the item to the list of displayed connections and add it to the ListBox for the user to select
                            Call pDisplayedConnections.Add(Connections(j).DeviceId)
                            Dim NewItem As New ListBoxItem()
                            NewItem.Content = Connections(j).DisplayName
                            NewItem.Tag = ManufacturerDexcom & ":" & Connections(j).InterfaceName & ":" & Connections(j).DeviceId
                            Call lstConnectedDevices.Items.Add(NewItem)
                        End If

                        DevicesFound += 1
                    End If
                Next
            Next
        End If

        ' If there aren't any new devices found show the NoDevicesFound label
        If DevicesFound = 0 Then
            If lstConnectedDevices.Items(0).Content <> NoDevicesFoundLabel Then
                lstConnectedDevices.IsEnabled = False
                pnlSerialNumber.Visibility = Visibility.Collapsed
                Call lstConnectedDevices.Items.Clear()
                Call pDisplayedConnections.Clear()
                Dim NewItem As New ListBoxItem()
                NewItem.Content = NoDevicesFoundLabel
                Call lstConnectedDevices.Items.Add(NewItem)
            End If
        ElseIf DevicesFound > 0 And lstConnectedDevices.Items(0).Content = NoDevicesFoundLabel Then
            lstConnectedDevices.IsEnabled = True
            Call lstConnectedDevices.Items.RemoveAt(0)
        End If
    End Sub

    ''' <summary>
    ''' Enrolls a device by connecting to it and adding it to Settings
    ''' </summary>
    ''' <param name="Manufacturer">The manufacturer of the device</param>
    ''' <param name="DeviceInterface">The device interfance</param>
    ''' <param name="DeviceConnection">The connection structure from the device interface</param>
    ''' <returns></returns>
    Private Async Function EnrollDevice(ByVal Manufacturer As String, ByVal DeviceInterface As String, ByVal DeviceConnection As DeviceInterface.DeviceConnection) As Task(Of Boolean)
        If Manufacturer = ManufacturerDexcom Then
            Dim NewDevice As DexcomDevice

            If DeviceInterface = USBInterface.InterfaceName Then
                ' Create the device, connect to it, and add it to Settings
                NewDevice = New DexcomDevice(DeviceInterface, "", DeviceConnection.DeviceId)
                If Await NewDevice.Connect() Then
                    Call Settings.AddEnrolledDevice(NewDevice)
                    Await NewDevice.Disconnect()
                    Return True
                End If
            End If
        End If

        Return False
    End Function
End Class
