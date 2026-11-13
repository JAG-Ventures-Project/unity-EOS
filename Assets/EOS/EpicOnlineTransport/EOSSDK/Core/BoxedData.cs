// Copyright Epic Games, Inc. All Rights Reserved.

namespace Epic.OnlineServices
{
	internal sealed class BoxedData
	{
		public object Data { get; private set; }

		public BoxedData(object data)
		{
			Data = data;
		}
	}
}
