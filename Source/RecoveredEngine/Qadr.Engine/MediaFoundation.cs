using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Qadr.Core;

namespace Qadr.Engine;

internal sealed class MediaFoundation : IDisposable
{
	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate uint Release(nint self);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int GuidValue(nint self, in Guid key, in Guid value);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int StringValue(nint self, in Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int UIntValue(nint self, in Guid key, uint value);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int LongValue(nint self, in Guid key, ulong value);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int GetString(nint self, in Guid key, out nint value, out uint length);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int GetLong(nint self, in Guid key, out ulong value);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int GetUInt(nint self, in Guid key, out uint value);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int Select(nint self, uint index, [MarshalAs(UnmanagedType.Bool)] bool enabled);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int SetType(nint self, uint index, nint reserved, nint type);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int CurrentType(nint self, uint index, out nint type);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int NativeType(nint self, uint index, uint typeIndex, out nint type);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int Read(nint self, uint index, uint control, out uint stream, out uint flags, out long stamp, out nint sample);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int Buffer(nint self, out nint buffer);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int LockBuffer(nint self, out nint bytes, out uint maximum, out uint current);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int Simple(nint self);

	private static readonly Guid SourceType = new Guid("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");

	private static readonly Guid VideoCapture = new Guid("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");

	private static readonly Guid Symbolic = new Guid("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f");

	private static readonly Guid Friendly = new Guid("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");

	private static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");

	private static readonly Guid Video = new Guid("73646976-0000-0010-8000-00aa00389b71");

	private static readonly Guid Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");

	private static readonly Guid Rgb32 = new Guid("00000016-0000-0010-8000-00aa00389b71");

	private static readonly Guid Size = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");

	private static readonly Guid Stride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");

	private static readonly Guid Processing = new Guid("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");

	private static readonly Guid FrameRate = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");

	private static readonly Guid LowLatency = new Guid("9c27891a-ed7a-40e1-88e8-b22727a024ee");

	private const uint FirstVideo = 4294967292u;

	private nint source;

	private nint reader;

	private readonly object sourceLock = new object();

	private readonly Thread thread;

	private readonly TaskCompletionSource ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly Action<byte[], int, int> frame;

	private readonly Action<string> fail;

	private readonly int targetFps;

	private volatile bool stopping;

	public double NominalFps { get; private set; }

	public Task Ready => ready.Task;

	[DllImport("mfplat.dll")]
	private static extern int MFStartup(uint version, uint flags);

	[DllImport("mfplat.dll")]
	private static extern int MFShutdown();

	[DllImport("mfplat.dll")]
	private static extern int MFCreateAttributes(out nint attributes, uint count);

	[DllImport("mfplat.dll")]
	private static extern int MFCreateMediaType(out nint type);

	[DllImport("mf.dll")]
	private static extern int MFEnumDeviceSources(nint attributes, out nint devices, out uint count);

	[DllImport("mf.dll")]
	private static extern int MFCreateDeviceSource(nint attributes, out nint source);

	[DllImport("mfreadwrite.dll")]
	private static extern int MFCreateSourceReaderFromMediaSource(nint source, nint attributes, out nint reader);

	[DllImport("ole32.dll")]
	private static extern int CoInitializeEx(nint reserved, uint flags);

	[DllImport("ole32.dll")]
	private static extern void CoUninitialize();

	private static T Method<T>(nint obj, int index) where T : Delegate
	{
		return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), index * IntPtr.Size));
	}

	private static void Drop(nint obj)
	{
		if (obj != 0)
		{
			Method<Release>(obj, 2)(obj);
		}
	}

	private static void Check(int hr)
	{
		if (hr < 0)
		{
			Marshal.ThrowExceptionForHR(hr);
		}
	}

	private static string String(nint obj, Guid key)
	{
		Check(Method<GetString>(obj, 13)(obj, in key, out var value, out var length));
		try
		{
			return Marshal.PtrToStringUni(value, (int)length) ?? "Camera";
		}
		finally
		{
			Marshal.FreeCoTaskMem(value);
		}
	}

	public static CameraDevice[] Discover()
	{
		Check(CoInitializeEx(0, 0u));
		Check(MFStartup(131184u, 0u));
		nint attributes = 0;
		nint devices = 0;
		uint count = 0u;
		try
		{
			Check(MFCreateAttributes(out attributes, 1u));
			Check(Method<GuidValue>(attributes, 24)(attributes, in SourceType, in VideoCapture));
			Check(MFEnumDeviceSources(attributes, out devices, out count));
			List<CameraDevice> list = new List<CameraDevice>();
			for (int i = 0; i < count; i++)
			{
				nint obj = Marshal.ReadIntPtr(devices, i * IntPtr.Size);
				list.Add(new CameraDevice(String(obj, Symbolic), String(obj, Friendly)));
			}
			return list.ToArray();
		}
		finally
		{
			for (int j = 0; j < count; j++)
			{
				Drop(Marshal.ReadIntPtr(devices, j * IntPtr.Size));
			}
			if (devices != 0)
			{
				Marshal.FreeCoTaskMem(devices);
			}
			Drop(attributes);
			MFShutdown();
			CoUninitialize();
		}
	}

	public MediaFoundation(string id, Action<byte[], int, int> frame, Action<string> fail, int targetFps = 30)
	{
		MediaFoundation mediaFoundation = this;
		this.frame = frame;
		this.fail = fail;
		this.targetFps = targetFps;
		thread = new Thread(() =>
		{
			mediaFoundation.Capture(id);
		})
		{
			IsBackground = true,
			Name = "Qadr Media Foundation capture"
		};
		thread.Start();
	}

	private void Capture(string id)
	{
		nint attributes = 0;
		nint attributes2 = 0;
		nint type = 0;
		bool flag = false;
		bool flag2 = false;
		try
		{
			Check(CoInitializeEx(0, 0u));
			flag2 = true;
			Check(MFStartup(131184u, 0u));
			flag = true;
			Check(MFCreateAttributes(out attributes, 2u));
			Check(Method<GuidValue>(attributes, 24)(attributes, in SourceType, in VideoCapture));
			Check(Method<StringValue>(attributes, 25)(attributes, in Symbolic, id));
			lock (sourceLock)
			{
				Check(MFCreateDeviceSource(attributes, out source));
			}
			Check(MFCreateAttributes(out attributes2, 2u));
			Check(Method<UIntValue>(attributes2, 21)(attributes2, in Processing, 1u));
			Check(Method<UIntValue>(attributes2, 21)(attributes2, in LowLatency, 1u));
			Check(MFCreateSourceReaderFromMediaSource(source, attributes2, out reader));
			Check(Method<Select>(reader, 4)(reader, 4294967294u, enabled: false));
			Check(Method<Select>(reader, 4)(reader, 4294967292u, enabled: true));
			nint num = 0;
			double num2 = double.NegativeInfinity;
			ulong num3 = 0uL;
			ulong num4 = 0uL;
			nint type2;
			for (uint num5 = 0u; num5 < 256 && Method<NativeType>(reader, 5)(reader, 4294967292u, num5, out type2) >= 0; num5++)
			{
				if (Method<GetLong>(type2, 8)(type2, in Size, out var value) >= 0)
				{
					uint width = (uint)(value >> 32);
					uint height = (uint)value;
					double num6 = 0.0;
					if (Method<GetLong>(type2, 8)(type2, in FrameRate, out var value2) >= 0 && (int)value2 != 0)
					{
						num6 = (double)(value2 >> 32) / (double)(uint)value2;
					}
					double num7 = CaptureMode.Score(width, height, num6, targetFps);
					if (num7 > num2)
					{
						Drop(num);
						num = type2;
						num2 = num7;
						num3 = value;
						num4 = ((num6 > 0.0) ? value2 : 0);
						continue;
					}
				}
				Drop(type2);
			}
			if (num != 0)
			{
				try
				{
					Check(Method<SetType>(reader, 7)(reader, 4294967292u, 0, num));
				}
				finally
				{
					Drop(num);
				}
			}
			Check(MFCreateMediaType(out type));
			Check(Method<GuidValue>(type, 24)(type, in MajorType, in Video));
			Check(Method<GuidValue>(type, 24)(type, in Subtype, in Rgb32));
			if (num3 != 0L)
			{
				Check(Method<LongValue>(type, 22)(type, in Size, num3));
			}
			if (num4 != 0L)
			{
				Check(Method<LongValue>(type, 22)(type, in FrameRate, num4));
			}
			int num8 = Method<SetType>(reader, 7)(reader, 4294967292u, 0, type);
			if (num8 < 0 && num3 != 0L)
			{
				Drop(type);
				type = 0;
				Check(MFCreateMediaType(out type));
				Check(Method<GuidValue>(type, 24)(type, in MajorType, in Video));
				Check(Method<GuidValue>(type, 24)(type, in Subtype, in Rgb32));
				num8 = Method<SetType>(reader, 7)(reader, 4294967292u, 0, type);
			}
			Check(num8);
			Drop(type);
			type = 0;
			Check(Method<CurrentType>(reader, 6)(reader, 4294967292u, out type));
			Check(Method<GetLong>(type, 8)(type, in Size, out var value3));
			int num9 = (int)(value3 >> 32);
			int num10 = (int)(value3 & 0xFFFFFFFFu);
			int num11 = num9 * 4;
			if (Method<GetUInt>(type, 7)(type, in Stride, out var value4) >= 0)
			{
				num11 = (int)value4;
			}
			if (num9 <= 0 || num10 <= 0 || num9 > 8192 || num10 > 8192 || Math.Abs(num11) < num9 * 4)
			{
				throw new IOException("Camera returned an unsupported frame size.");
			}
			if (Method<GetLong>(type, 8)(type, in FrameRate, out var value5) >= 0 && (int)value5 != 0)
			{
				NominalFps = (double)(value5 >> 32) / (double)(uint)value5;
			}
			byte[] array = Array.Empty<byte>();
			byte[] array2 = Array.Empty<byte>();
			ready.TrySetResult();
			while (!stopping)
			{
				nint sample = 0;
				nint buffer = 0;
				try
				{
					Check(Method<Read>(reader, 9)(reader, 4294967292u, 0u, out var stream, out var flags, out var _, out sample));
					if ((flags & 3) != 0)
					{
						throw new IOException("The camera disconnected.");
					}
					if (sample == 0)
					{
						continue;
					}
					Check(Method<Buffer>(sample, 41)(sample, out buffer));
					Check(Method<LockBuffer>(buffer, 3)(buffer, out var bytes, out stream, out var current));
					try
					{
						int num12 = Math.Abs(num11);
						if (current < (long)num12 * (long)num10)
						{
							throw new IOException("Camera frame is incomplete.");
						}
						if (array.Length != current)
						{
							array = new byte[current];
						}
						Marshal.Copy(bytes, array, 0, array.Length);
						if (num9 <= 640 && num10 <= 480 && num11 == num9 * 4)
						{
							if (!stopping)
							{
								frame(array, num9, num10);
							}
							continue;
						}
						double val = Math.Min(640.0 / (double)num9, 480.0 / (double)num10);
						int num13 = Math.Max(1, (int)((double)num9 * Math.Min(1.0, val)));
						int num14 = Math.Max(1, (int)((double)num10 * Math.Min(1.0, val)));
						if (array2.Length != num13 * num14 * 4)
						{
							array2 = new byte[num13 * num14 * 4];
						}
						for (int i = 0; i < num14; i++)
						{
							int num15 = i * num10 / num14;
							if (num11 < 0)
							{
								num15 = num10 - 1 - num15;
							}
							for (int j = 0; j < num13; j++)
							{
								int num16 = num15 * num12 + j * num9 / num13 * 4;
								int num17 = (i * num13 + j) * 4;
								array2[num17] = array[num16];
								array2[num17 + 1] = array[num16 + 1];
								array2[num17 + 2] = array[num16 + 2];
								array2[num17 + 3] = byte.MaxValue;
							}
						}
						if (!stopping)
						{
							frame(array2, num13, num14);
						}
					}
					finally
					{
						Method<Simple>(buffer, 4)(buffer);
					}
				}
				finally
				{
					Drop(buffer);
					Drop(sample);
				}
			}
		}
		catch (Exception ex)
		{
			ready.TrySetException(ex);
			if (!stopping)
			{
				fail((ex is UnauthorizedAccessException) ? "Windows blocked camera access. Check Camera privacy settings and allow desktop apps." : ex.Message);
			}
		}
		finally
		{
			Drop(type);
			Drop(reader);
			reader = 0;
			lock (sourceLock)
			{
				if (source != 0)
				{
					Method<Simple>(source, 12)(source);
				}
				Drop(source);
				source = 0;
			}
			Drop(attributes2);
			Drop(attributes);
			if (flag)
			{
				MFShutdown();
			}
			if (flag2)
			{
				CoUninitialize();
			}
		}
	}

	public void Dispose()
	{
		stopping = true;
		lock (sourceLock)
		{
			if (source != 0)
			{
				try
				{
					Method<Simple>(source, 12)(source);
				}
				catch (COMException)
				{
				}
			}
		}
		if (!thread.Join(1500))
		{
			throw new IOException("The previous camera is still closing. Restart Qadr before choosing another camera.");
		}
	}
}
