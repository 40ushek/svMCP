using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Tekla.Structures;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Extensions;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tekla Structures tool to create connections.")]
	public class TeklaCreateConnectionTool
	{
		private const int MaxConnectionsPerCall = 50;

		[Description("Creates connections based on the provided parameters. A connection is something that connects one primary part to many secondary parts.")]
		public static ToolExecutionResult CreateConnections([Description("Property sets of the connections to be created. It is a list of dictionaries in JSON format (max 50 entries).Required parameters: ConnectionName, ConnectionNumber, PrimaryPartIdentifier, SecondaryPartIdentifiers, FileExtension.Optional: AttributesFile, PropertySet that its a dictionary of additional properties to set on the connection. Key is the property name, value is its value.CRITICAL: Do NOT include optional properties (Name, Profile, Material, Class, etc.) in the JSON unless the user explicitly requests them as overrides.Example: [ { \"ConnectionName\": \"U.S. Seat Joint\", \"ConnectionNumber\": 72, \"PrimaryPartIdentifier\": 12345, \"SecondaryPartIdentifiers\": [54321, 54235], \"FileExtension\": \".j72\" } ]")] string connectionCreationInputListString)
		{
			if (!connectionCreationInputListString.TryConvertFromJson<List<ConnectionCreationInput>>(out var connectionCreationInputList))
			{
				return ToolExecutionResult.CreateErrorResult("Failed to parse 'connectionCreationInputListString' argument. Ensure it is a valid JSON list of ConnectionCreationInput objects.");
			}
			if (connectionCreationInputList.Count > 50)
			{
				return ToolExecutionResult.CreateErrorResult($"Too many model objects in one call. Maximum is {50}, received {connectionCreationInputList.Count}. Split into multiple calls.");
			}
			Model model = new Model();
			List<object> createdConnections = new List<object>();
			List<object> failedConnections = new List<object>();
			foreach (ConnectionCreationInput connectionInput in connectionCreationInputList)
			{
				if (!TryCreateConnection(model, connectionInput, out var connection, out var errorMessage))
				{
					failedConnections.Add(new
					{
						ConnectionNumber = connectionInput.ConnectionNumber,
						Error = errorMessage
					});
					continue;
				}
				switch (connection.Status)
				{
				case ConnectionStatusEnum.STATUS_OK:
					createdConnections.Add(new
					{
						ConnectionId = connection.Identifier.ToString(),
						Warning = errorMessage,
						Status = "OK"
					});
					break;
				case ConnectionStatusEnum.STATUS_WARNING:
					createdConnections.Add(new
					{
						ConnectionId = connection.Identifier.ToString(),
						Warning = errorMessage,
						Status = "WARNING"
					});
					break;
				case ConnectionStatusEnum.STATUS_ERROR:
					createdConnections.Add(new
					{
						ConnectionId = connection.Identifier.ToString(),
						Warning = errorMessage,
						Status = "ERROR"
					});
					break;
				default:
					createdConnections.Add(new
					{
						ConnectionId = connection.Identifier.ToString(),
						Warning = errorMessage,
						Status = "UNKNOWN"
					});
					break;
				}
			}
			if (createdConnections.Count == 0)
			{
				return ToolExecutionResult.CreateErrorResult($"No connections were created. {failedConnections.Count} failures.", null, new
				{
					FailedConnections = failedConnections
				});
			}
			model.CommitChanges("(TMA) CreateConnections");
			return ToolExecutionResult.CreateSuccessResult($"Created {createdConnections.Count} out of {connectionCreationInputList.Count} connections.", new
			{
				CreatedConnections = createdConnections,
				FailedConnections = failedConnections
			});
		}

		private static bool TryCreateConnection(Model model, ConnectionCreationInput connectionCreationInput, out Connection connection, out string errorMessage)
		{
			connection = null;
			try
			{
				connection = new Connection
				{
					Name = connectionCreationInput.ConnectionName,
					Number = connectionCreationInput.ConnectionNumber
				};
				ModelObject primaryPart = model.SelectModelObject(new Identifier(connectionCreationInput.PrimaryPartIdentifier));
				if (primaryPart == null)
				{
					errorMessage = $"Primary part with identifier {connectionCreationInput.PrimaryPartIdentifier} not found.";
					return false;
				}
				connection.SetPrimaryObject(primaryPart);
				List<int> secondaryPartIdentifiers = connectionCreationInput.SecondaryPartIdentifiers;
				if (secondaryPartIdentifiers != null && secondaryPartIdentifiers.Count <= 0)
				{
					errorMessage = "At least one secondary part identifier must be provided.";
					return false;
				}
				List<ModelObject> secondaryParts = new List<ModelObject>();
				foreach (int secondaryPartId in connectionCreationInput.SecondaryPartIdentifiers)
				{
					ModelObject secondaryPart = model.SelectModelObject(new Identifier(secondaryPartId));
					if (secondaryPart == null)
					{
						errorMessage = $"Secondary part with identifier {secondaryPartId} not found.";
						return false;
					}
					secondaryParts.Add(secondaryPart);
				}
				if (secondaryParts.Count == 0)
				{
					errorMessage = "No valid secondary parts found.";
					return false;
				}
				if (secondaryParts.Count == 1)
				{
					connection.SetSecondaryObject(secondaryParts[0]);
				}
				else
				{
					connection.SetSecondaryObjects(new ArrayList(secondaryParts));
				}
				if (!string.IsNullOrWhiteSpace(connectionCreationInput.AttributesFile))
				{
					connection.LoadAttributesFromFile(connectionCreationInput.AttributesFile);
				}
				StringBuilder messageBuilder = new StringBuilder();
				foreach (KeyValuePair<string, string> property in connectionCreationInput.PropertySet)
				{
					try
					{
						if (!PropertyAccessHelper.TrySetPropertyValue(connection, property.Key, property.Value))
						{
							messageBuilder.AppendLine("Property " + property.Key + " could not be set.");
						}
					}
					catch (Exception ex)
					{
						messageBuilder.AppendLine("Error setting property " + property.Key + ": " + ex.Message);
					}
				}
				if (!connection.Insert())
				{
					errorMessage = "Failed to insert connection " + connectionCreationInput.ConnectionName + ".";
					return false;
				}
				errorMessage = messageBuilder.ToString();
				return true;
			}
			catch (Exception ex2)
			{
				errorMessage = "Failed for connection " + connectionCreationInput.ConnectionName + ": " + ex2.Message;
				return false;
			}
		}
	}
}
