using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal interface IDimensionRule
{
    DimensionRuleEvaluation Calculate(DimensionRuleContext context);
}
