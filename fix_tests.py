path = 'tests/Clawbot.Infrastructure.Tests/Channels/PancakeConfigResolverTests.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace(
    'using NSubstitute;',
    'using NSubstitute;\nusing Microsoft.Extensions.Logging.Abstractions;')

# Fix 1st test - empty dict, single line
content = content.replace(
    'new PancakeConfigResolver(fx.Db, enc, Config(new Dictionary<string, string?>()));',
    'new PancakeConfigResolver(fx.Db, enc, Config(new Dictionary<string, string?>()), NullLogger<PancakeConfigResolver>.Instance);')

# Fix 2nd test - multiline
content = content.replace(
    '        }));\n\n        var result = await sut.ResolveAsync(Guid.NewGuid());',
    '        }), NullLogger<PancakeConfigResolver>.Instance);\n\n        var result = await sut.ResolveAsync(Guid.NewGuid());')

# Fix 3rd test - Empty tenant, multiline with single key
content = content.replace(
    '        }));\n\n        var result = await sut.ResolveAsync(Guid.Empty);',
    '        }), NullLogger<PancakeConfigResolver>.Instance);\n\n        var result = await sut.ResolveAsync(Guid.Empty);')

# Fix 4th test - null case
content = content.replace(
    'new PancakeConfigResolver(fx.Db, enc, Config(new Dictionary<string, string?>()));',
    'new PancakeConfigResolver(fx.Db, enc, Config(new Dictionary<string, string?>()), NullLogger<PancakeConfigResolver>.Instance);')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
print('Fixed')
