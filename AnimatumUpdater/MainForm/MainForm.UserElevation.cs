﻿using Animatum.Updater.Common;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Animatum.Updater
{
    public partial class MainForm
    {
        void StartSelfElevated()
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                ErrorDialog = true,
                ErrorDialogParentHandle = Handle,
                FileName = VersionTools.SelfLocation
            };

            if (needElevation)
            {
                // elevate to administrator
                psi.Verb = "runas";

                // check if UAC is enabled on the computer, if not throw an error
                bool canUAC = false;

                try
                {
                    if (Win32.AtLeastVista())
                    {
                        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"))
                        {
                            // see if UAC is enabled
                            if (key != null)
                                canUAC = (int)key.GetValue("EnableLUA", null) != 0;
                        }
                    }
                    else
                    {
                        // Assume XP / 2000 limited user can use the RunAs box
                        // this may or may not be true.

                        //TODO: make this more robust
                        // see: http://www.windowsnetworking.com/kbase/WindowsTips/Windows2003/AdminTips/Admin/DisablingtheRunAsCommand.html
                        canUAC = true;
                    }
                }
                catch { }

                if (!canUAC)
                {
                    //TODO: use a more specific error to tell the user
                    // to re-enable UAC on their computer.
                    error = clientLang.AdminError;
                    ShowFrame(Frame.Error);
                    return;
                }
            }

            try
            {
                if (IsNewSelf)
                    psi.Arguments += " /ns";

                Process.Start(psi);
                Close();
            }
            catch (Exception ex)
            {
                // the process couldn't be started. This happens for 1 of 3 reasons:

                // 1. The user cancelled the UAC box
                // 2. The limited user tried to elevate to an Admin that has a blank password
                // 3. The limited user tries to elevate as a Guest account
                error = clientLang.AdminError;
                errorDetails = ex.Message;

                ShowFrame(Frame.Error);
            }
        }

        bool NeedElevationToUpdate()
        {
            // if only updating local user files, no elevation is needed
            if (IsAdmin || OnlyUpdatingLocalUser())
                return false;

            // UAC Shield on next button for Windows Vista+
            if (Win32.AtLeastVista())
                Win32.SetButtonShield(btnNext, true);

            return true;
        }

        /// <summary>Checks if the current process has the necessary permissions to a folder. This allows "custom" elevation of limited users -- allowing them control over normally restricted folders.</summary>
        /// <param name="folder">The full path to the folder to check.</param>
        /// <returns>True if the user has permission, false if not.</returns>
        static bool HaveFolderPermissions(string folder)
        {
            try
            {
                const FileSystemRights RightsNeeded = FileSystemRights.Traverse |
                                                      FileSystemRights.DeleteSubdirectoriesAndFiles |
                                                      FileSystemRights.ListDirectory | FileSystemRights.CreateFiles |
                                                      FileSystemRights.CreateDirectories |
                                                      FileSystemRights.Modify; //Read, ExecuteFile, Write, Delete

                FileSystemSecurity security = Directory.GetAccessControl(folder);

                var rules = security.GetAccessRules(true, true, typeof(NTAccount));

                var currentuser = new WindowsPrincipal(WindowsIdentity.GetCurrent());

                FileSystemRights RightsHave = 0;
                FileSystemRights RightsDontHave = 0;

                foreach (FileSystemAccessRule rule in rules)
                {
                    // First check to see if the current user is even in the role, if not, skip
                    if (rule.IdentityReference.Value.StartsWith("S-1-"))
                    {
                        var sid = new SecurityIdentifier(rule.IdentityReference.Value);
                        if (!currentuser.IsInRole(sid))
                            continue;
                    }
                    else
                    {
                        if (!currentuser.IsInRole(rule.IdentityReference.Value))
                            continue;
                    }

                    if (rule.AccessControlType == AccessControlType.Deny)
                        RightsDontHave |= rule.FileSystemRights;
                    else
                        RightsHave |= rule.FileSystemRights;
                }

                // exclude "RightsDontHave"
                RightsHave &= ~RightsDontHave;

                //Note: We're "XOR"ing with RightsNeeded to eliminate permissions that
                //      "RightsHave" and "RightsNeeded" have in common. Then we're
                //      ANDing that result with RightsNeeded to get permissions in
                //      "RightsNeeded" that are missing from "RightsHave". The result
                //      should be 0 if the user has RightsNeeded over the folder (even
                //      if "RightsHave" has flags that aren't present in the 
                //      "RightsNeeded" -- which can happen because "RightsNeeded" isn't
                //      *every* possible flag).

                // Check if the user has full control over the folder.
                return ((RightsHave ^ RightsNeeded) & RightsNeeded) == 0;
            }
            catch
            {
                return false;
            }
        }

        //TODO: Expand this function to allow for artificially elevated limited users.
        //      For example, a limited user given the permission to write to HKLM
        //      (something that is normally forbidden).
        bool OnlyUpdatingLocalUser()
        {
            // if installing
            //         - system folders
            //         - non-user registry
            //         - Windows Services
            //         - COM files
            // then return false
            // Also note how we're excluding the "BaseDir".
            // This is because the base directory may or may not be in the userprofile
            // directory (or otherwise writable by the user), thus it needs a separate check.
            // Ditto for the "client.wyc" file.
            if (((updateFrom.InstallingTo | InstallingTo.BaseDir) ^ InstallingTo.BaseDir) != 0)
                return false;

            string userProfileFolder = SystemFolders.GetUserProfile();

            // if the basedir isn't in the userprofile folder (C:\Users\UserName)
            // OR
            // if the folder isn't in the full control of the current user
            // THEN
            // we're not "only updating the local user
            if ((updateFrom.InstallingTo & InstallingTo.BaseDir) != 0 && !(SystemFolders.IsDirInDir(userProfileFolder, baseDirectory) || HaveFolderPermissions(baseDirectory)))
                return false;

            return true;
        }
    }
}
