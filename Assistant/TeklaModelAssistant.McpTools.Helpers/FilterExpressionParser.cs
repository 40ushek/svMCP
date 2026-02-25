using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Filtering;

namespace TeklaModelAssistant.McpTools.Helpers
{
	public class FilterExpressionParser
	{
		private List<Token> _tokens;

		private int _position;

		public FilterNode Parse(List<Token> tokens)
		{
			if (tokens == null || tokens.Count == 0)
			{
				throw new ArgumentException("Tokens list cannot be null or empty.", "tokens");
			}
			_tokens = tokens;
			_position = 0;
			FilterNode root = ParseExpression();
			if (Current().Type != TokenType.EOF)
			{
				throw new FilterExpressionException($"Unexpected token {Current()} after parsing completed.");
			}
			return root;
		}

		private FilterNode ParseExpression()
		{
			List<FilterNode> nodes = new List<FilterNode>();
			List<BinaryFilterOperatorType> operators = new List<BinaryFilterOperatorType>();
			nodes.Add(ParseTerm());
			while (Current().Type == TokenType.LogicalOperator)
			{
				string opValue = Consume(TokenType.LogicalOperator).Value;
				BinaryFilterOperatorType op = ((!(opValue == "OR")) ? BinaryFilterOperatorType.BOOLEAN_AND : BinaryFilterOperatorType.BOOLEAN_OR);
				operators.Add(op);
				nodes.Add(ParseTerm());
			}
			if (nodes.Count == 1)
			{
				return nodes[0];
			}
			return new GroupNode
			{
				Children = nodes,
				Operators = operators
			};
		}

		private FilterNode ParseTerm()
		{
			if (Current().Type == TokenType.OpenParen)
			{
				return ParseGroup();
			}
			if (Current().Type == TokenType.Expression)
			{
				return ParseSimpleExpressionSequence();
			}
			throw new FilterExpressionException($"Unexpected token {Current()}. Expected '(' or expr.");
		}

		private FilterNode ParseGroup()
		{
			Consume(TokenType.OpenParen);
			FilterNode innerExpression = ParseExpression();
			Consume(TokenType.CloseParen);
			if (innerExpression is SimpleExpressionNode)
			{
				return new GroupNode
				{
					Children = new List<FilterNode> { innerExpression },
					Operators = new List<BinaryFilterOperatorType>()
				};
			}
			return innerExpression;
		}

		private FilterNode ParseSimpleExpressionSequence()
		{
			List<FilterNode> nodes = new List<FilterNode>();
			List<BinaryFilterOperatorType> operators = new List<BinaryFilterOperatorType>();
			Token token = Consume(TokenType.Expression);
			string[] expressionParts = token.Value.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < expressionParts.Length; i++)
			{
				var (node, logicalOp) = ParseSingleExpressionString(expressionParts[i], token.Position);
				nodes.Add(node);
				if (logicalOp.HasValue)
				{
					operators.Add(logicalOp.Value);
				}
			}
			if (operators.Count > 0 && operators.Count >= nodes.Count)
			{
				operators.RemoveAt(operators.Count - 1);
			}
			if (nodes.Count == 1)
			{
				return nodes[0];
			}
			return new GroupNode
			{
				Children = nodes,
				Operators = operators
			};
		}

		private (SimpleExpressionNode node, BinaryFilterOperatorType? logicalOp) ParseSingleExpressionString(string expressionString, int position)
		{
			if (string.IsNullOrWhiteSpace(expressionString))
			{
				throw new FilterExpressionException($"Empty expression at position {position}");
			}
			string[] components = (from s in expressionString.Split('|')
				select s.Trim()).ToArray();
			if (components.Length < 4)
			{
				throw new FilterExpressionException("Invalid expression format: " + expressionString);
			}
			SimpleExpressionNode node = new SimpleExpressionNode
			{
				Category = components[0],
				Property = components[1],
				Operator = components[2],
				Value = components[3]
			};
			BinaryFilterOperatorType? logicalOp = null;
			if (components.Length > 4)
			{
				string opStr = components[4].ToUpper();
				logicalOp = ((!(opStr == "OR")) ? BinaryFilterOperatorType.BOOLEAN_AND : BinaryFilterOperatorType.BOOLEAN_OR);
			}
			return (node: node, logicalOp: logicalOp);
		}

		private Token Current()
		{
			if (_position >= _tokens.Count)
			{
				return _tokens[_tokens.Count - 1];
			}
			return _tokens[_position];
		}

		private Token Consume(TokenType expectedType)
		{
			Token token = Current();
			if (token.Type != expectedType)
			{
				throw new FilterExpressionException($"Expected {expectedType} but got {token.Type}");
			}
			_position++;
			return token;
		}
	}
}
