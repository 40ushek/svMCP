using Tekla.Structures.Model;

namespace TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer
{
	public static class SerializerFactory
	{
		public static ISerializer CreateSerializer(object obj)
		{
			ISerializer result;
			if (!(obj is Seam))
			{
				if (!(obj is Beam))
				{
					if (!(obj is Component))
					{
						if (!(obj is Contour))
						{
							if (!(obj is Offset))
							{
								if (!(obj is Connection))
								{
									if (!(obj is ReferenceModelObject))
									{
										if (!(obj is Detail))
										{
											if (!(obj is Assembly))
											{
												if (!(obj is BoltArray))
												{
													if (obj is BoltXYList)
													{
														ISerializer serializer = new BoltXYListSerializer();
														result = serializer;
													}
													else
													{
														ISerializer serializer = new GenericDataSerializer();
														result = serializer;
													}
												}
												else
												{
													ISerializer serializer = new BoltArraySerializer();
													result = serializer;
												}
											}
											else
											{
												ISerializer serializer = new AssemblySerializer();
												result = serializer;
											}
										}
										else
										{
											ISerializer serializer = new DetailSerializer();
											result = serializer;
										}
									}
									else
									{
										ISerializer serializer = new ReferenceModelObjectSerializer();
										result = serializer;
									}
								}
								else
								{
									ISerializer serializer = new ConnectionSerializer();
									result = serializer;
								}
							}
							else
							{
								ISerializer serializer = new OffsetSerializer();
								result = serializer;
							}
						}
						else
						{
							ISerializer serializer = new ContourSerializer();
							result = serializer;
						}
					}
					else
					{
						ISerializer serializer = new ComponentSerializer();
						result = serializer;
					}
				}
				else
				{
					ISerializer serializer = new BeamSerializer();
					result = serializer;
				}
			}
			else
			{
				ISerializer serializer = new SeamSerializer();
				result = serializer;
			}
			return result;
		}
	}
}
