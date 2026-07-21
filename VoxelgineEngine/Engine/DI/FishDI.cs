using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Voxelgine.Engine.DI
{
	// Dependency injection implementation
	public sealed class FishDI : IDisposable
	{
		HostApplicationBuilder builder;
		IHost FHost;

		public FishDI()
		{
			builder = Host.CreateApplicationBuilder();
		}

		public void AddScoped<TService, TImpl>() where TService : class where TImpl : class, TService
		{
			RegisterImplementationAndAlias<TService, TImpl>(ServiceLifetime.Scoped);
		}

		public void AddSingleton<TService, TImpl>() where TService : class where TImpl : class, TService
		{
			RegisterImplementationAndAlias<TService, TImpl>(ServiceLifetime.Singleton);
		}

		public void AddSingleton<TService>(Func<IServiceProvider, TService> factory)
			where TService : class
		{
			ArgumentNullException.ThrowIfNull(factory);
			builder.Services.AddSingleton(factory);
		}

		public void AddTransient<TService, TImpl>() where TService : class where TImpl : class, TService
		{
			RegisterImplementationAndAlias<TService, TImpl>(ServiceLifetime.Transient);
		}

		private void RegisterImplementationAndAlias<TService, TImpl>(ServiceLifetime lifetime)
			where TService : class
			where TImpl : class, TService
		{
			builder.Services.Add(new ServiceDescriptor(typeof(TImpl), typeof(TImpl), lifetime));
			if (typeof(TService) != typeof(TImpl))
			{
				builder.Services.Add(new ServiceDescriptor(
					typeof(TService),
					provider => provider.GetRequiredService<TImpl>(),
					lifetime));
			}
		}

		public IHost Build()
		{
			if (FHost != null)
				throw new InvalidOperationException("The service host has already been built.");
			FHost = builder.Build();
			return FHost;
		}

		IServiceScope CurScope;

		public void CreateScope()
		{
			if (FHost == null)
				throw new InvalidOperationException("Build the service host before creating a scope.");
			if (CurScope != null)
				CurScope.Dispose();

			CurScope = FHost.Services.CreateScope();
		}

		public T GetRequiredService<T>()
		{
			if (CurScope == null)
				throw new InvalidOperationException("Create a service scope before resolving services.");
			return CurScope.ServiceProvider.GetRequiredService<T>();
		}

		public void Dispose()
		{
			CurScope?.Dispose();
			CurScope = null;
			FHost?.Dispose();
			FHost = null;
		}
	}
}
