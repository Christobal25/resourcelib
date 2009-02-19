using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.IO;

namespace Vestris.ResourceLib
{
    /// <summary>
    /// A version resource, RT_RCDATA
    /// </summary>
    public class VersionResource : Resource
    {
        ResourceTable _header = new ResourceTable();
        Kernel32.VS_FIXEDFILEINFO _fixedfileinfo;
        private Dictionary<string, ResourceTable> _resources = null;
        private byte[] _readBytes = null;

        public byte[] ReadBytes
        {
            get
            {
                return _readBytes;
            }
        }

        public ResourceTable Header
        {
            get
            {
                return _header;
            }
        }

        public Dictionary<string, ResourceTable> Resources
        {
            get
            {
                return _resources;
            }
        }

        /// <summary>
        /// A version resource
        /// </summary>
        public VersionResource(IntPtr hResource, IntPtr type, IntPtr name, ushort wIDLanguage, int size)
            : base(hResource, type, name, wIDLanguage, size)
        {
            IntPtr lpRes = Kernel32.LockResource(hResource);

            if (lpRes == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            Load(lpRes);
        }

        public VersionResource(string filename)
        {
            IntPtr hModule = IntPtr.Zero;

            try
            {
                // load DLL
                hModule = Kernel32.LoadLibraryEx(filename, IntPtr.Zero,
                    Kernel32.DONT_RESOLVE_DLL_REFERENCES | Kernel32.LOAD_LIBRARY_AS_DATAFILE);

                if (IntPtr.Zero == hModule)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                IntPtr hRes = Kernel32.FindResource(hModule, Marshal.StringToHGlobalUni("#1"), new IntPtr(Kernel32.RT_RCDATA));
                if (IntPtr.Zero == hRes)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                IntPtr hGlobal = Kernel32.LoadResource(hModule, hRes);
                if (IntPtr.Zero == hGlobal)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                IntPtr lpRes = Kernel32.LockResource(hGlobal);

                if (lpRes == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                _size = Kernel32.SizeofResource(hModule, hRes);
                if (_size <= 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                Load(lpRes);
            }
            finally
            {
                if (hModule != IntPtr.Zero)
                    Kernel32.FreeLibrary(hModule);
            }
        }

        /// <summary>
        /// Load a version resource, heavily inspired from http://www.codeproject.com/KB/dotnet/FastFileVersion.aspx
        /// </summary>
        /// <param name="lpRes"></param>
        public void Load(IntPtr lpRes)
        {
            _resources = new Dictionary<string, ResourceTable>();
            IntPtr pFixedFileInfo = _header.Load(lpRes);

            // save bytes for Bytes property
            _readBytes = new byte[_header.Header.wLength];
            Marshal.Copy(lpRes, _readBytes, 0, _header.Header.wLength);

            _fixedfileinfo = (Kernel32.VS_FIXEDFILEINFO)Marshal.PtrToStructure(
                pFixedFileInfo, typeof(Kernel32.VS_FIXEDFILEINFO));

            IntPtr pChild = ResourceUtil.Align(pFixedFileInfo.ToInt32() + _header.Header.wValueLength);

            while (pChild.ToInt32() < (lpRes.ToInt32() + _header.Header.wLength))
            {
                ResourceTable rc = new ResourceTable(pChild);
                switch (rc.Key)
                {
                    case "StringFileInfo":
                        rc = new StringFileInfo(pChild);
                        break;
                    default:
                        rc = new VarFileInfo(pChild);
                        break;
                }

                _resources.Add(rc.Key, rc);
                pChild = ResourceUtil.Align(pChild.ToInt32() + rc.Header.wLength);
            }
        }

        /// <summary>
        /// File version
        /// </summary>
        public string FileVersion
        {
            get
            {
                return string.Format("{0}.{1}.{2}.{3}",
                    (_fixedfileinfo.dwFileVersionMS & 0xffff0000) >> 16,
                    _fixedfileinfo.dwFileVersionMS & 0x0000ffff,
                    (_fixedfileinfo.dwFileVersionLS & 0xffff0000) >> 16,
                    _fixedfileinfo.dwFileVersionLS & 0x0000ffff);
            }
            set
            {
                UInt32 major = 0, minor = 0, build = 0, release = 0;
                string[] version_s = value.Split(".".ToCharArray(), 4);
                if (version_s.Length >= 1) major = UInt32.Parse(version_s[0]);
                if (version_s.Length >= 2) minor = UInt32.Parse(version_s[1]);
                if (version_s.Length >= 3) build = UInt32.Parse(version_s[2]);
                if (version_s.Length >= 4) release = UInt32.Parse(version_s[3]);
                _fixedfileinfo.dwFileVersionMS = (major << 16) + minor;
                _fixedfileinfo.dwFileVersionLS = (build << 16) + release;
            }
        }

        /// <summary>
        /// Product binary version
        /// </summary>
        public string ProductVersion
        {
            get
            {
                return string.Format("{0}.{1}.{2}.{3}",
                    (_fixedfileinfo.dwProductVersionMS & 0xffff0000) >> 16,
                    _fixedfileinfo.dwProductVersionMS & 0x0000ffff,
                    (_fixedfileinfo.dwProductVersionLS & 0xffff0000) >> 16,
                    _fixedfileinfo.dwProductVersionLS & 0x0000ffff);
            }
            set
            {
                UInt32 major = 0, minor = 0, build = 0, release = 0;
                string[] version_s = value.Split(".".ToCharArray(), 4);
                if (version_s.Length >= 1) major = UInt32.Parse(version_s[0]);
                if (version_s.Length >= 2) minor = UInt32.Parse(version_s[1]);
                if (version_s.Length >= 3) build = UInt32.Parse(version_s[2]);
                if (version_s.Length >= 4) release = UInt32.Parse(version_s[3]);
                _fixedfileinfo.dwProductVersionMS = (major << 16) + minor;
                _fixedfileinfo.dwProductVersionLS = (build << 16) + release;
            }
        }

        public override void Write(BinaryWriter w)
        {
            _header.Write(w);
            
            w.Write(ResourceUtil.GetBytes<Kernel32.VS_FIXEDFILEINFO>(_fixedfileinfo));
            ResourceUtil.PadToDWORD(w);

            Dictionary<string, ResourceTable>.Enumerator resourceEnum = _resources.GetEnumerator();
            while (resourceEnum.MoveNext())
            {
                resourceEnum.Current.Value.Write(w);
            }
        }

        public void Save(string filename)
        {
            IntPtr h = Kernel32.BeginUpdateResource(filename, false);

            if (h == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            byte[] data = GetBytes();

            if (!Kernel32.UpdateResource(h, "16", "#1",
                (ushort) ResourceUtil.MAKELANGID(Kernel32.LANG_NEUTRAL, Kernel32.SUBLANG_NEUTRAL),
                data, (uint)data.Length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (!Kernel32.EndUpdateResource(h, false))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
