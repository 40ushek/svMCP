using System;
using Tekla.Structures.Filtering;

namespace TeklaModelAssistant.McpTools.Helpers
{
	public class FilterAstBuilder
	{
		public BinaryFilterExpressionCollection BuildFromAst(FilterNode rootNode)
		{
			if (rootNode == null)
			{
				throw new ArgumentNullException("rootNode");
			}
			BinaryFilterExpressionCollection collection = new BinaryFilterExpressionCollection();
			if (rootNode is GroupNode group)
			{
				for (int i = 0; i < group.Children.Count; i++)
				{
					FilterNode child = group.Children[i];
					BinaryFilterOperatorType childOp = ((i >= group.Operators.Count) ? BinaryFilterOperatorType.EMPTY : group.Operators[i]);
					BuildNode(child, collection, childOp);
				}
			}
			else
			{
				if (!(rootNode is SimpleExpressionNode simple))
				{
					throw new FilterExpressionException("Invalid root node type");
				}
				BuildSimpleExpression(simple, collection, BinaryFilterOperatorType.BOOLEAN_AND);
			}
			return collection;
		}

		private void BuildNode(FilterNode node, BinaryFilterExpressionCollection collection, BinaryFilterOperatorType logicalOp)
		{
			if (node is SimpleExpressionNode simpleNode)
			{
				BuildSimpleExpression(simpleNode, collection, logicalOp);
				return;
			}
			if (node is GroupNode groupNode)
			{
				BuildGroupNode(groupNode, collection, logicalOp);
				return;
			}
			throw new FilterExpressionException("Unknown node type: " + node.GetType().Name);
		}

		private void BuildSimpleExpression(SimpleExpressionNode node, BinaryFilterExpressionCollection collection, BinaryFilterOperatorType logicalOp)
		{
			BinaryFilterExpression filterExpression = FilterHelper.CreateFilterExpressionPublic(node.Category, node.Property, node.Operator, node.Value);
			if (filterExpression == null)
			{
				throw new FilterExpressionException($"Failed to create filter expression for {node}", node.Category, node.Property, node.Operator, node.Value);
			}
			collection.Add(new BinaryFilterExpressionItem(filterExpression, logicalOp));
		}

		private void BuildGroupNode(GroupNode groupNode, BinaryFilterExpressionCollection collection, BinaryFilterOperatorType logicalOp)
		{
			if (groupNode.Children.Count == 0)
			{
				throw new FilterExpressionException("Empty group node found in AST");
			}
			BinaryFilterExpressionCollection nestedCollectionForGroup = new BinaryFilterExpressionCollection();
			for (int i = 0; i < groupNode.Children.Count; i++)
			{
				FilterNode child = groupNode.Children[i];
				BinaryFilterOperatorType childOp = ((i >= groupNode.Operators.Count) ? BinaryFilterOperatorType.EMPTY : groupNode.Operators[i]);
				BuildNode(child, nestedCollectionForGroup, childOp);
			}
			collection.Add(new BinaryFilterExpressionItem(nestedCollectionForGroup, logicalOp));
		}
	}
}
