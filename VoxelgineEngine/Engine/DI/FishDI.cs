using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Voxelgine.Engine.DI
{
	// Dependency injection implementation
	public class FishDI
	{
		HostApplicationBuilder builder;
		IHost FHost;

		public FishDI()
		{
			builder = Host.CreateApplicationBuilder();
		}

		public void AddScoped<TService, TImpl>() where TService : class where TImpl : class, TService
		{
			builder.Services.AddScoped<TService, TImpl>();
		}

		public void AddSingleton<TService, TImpl>() where TService : class where TImpl : class, TService
		{
			builder.Services.AddSingleton<TService, TImpl>();
		}

		public void AddTransient<TService, TImpl>() where TService : class where TImpl : class, TService
		{
			builder.Services.AddTransient<TService, TImpl>();
		}

		public IHost Build()
		{
			FHost = builder.Build();
			return FHost;
		}

		IServiceScope CurScope;

		public void CreateScope()
		{
			if (CurScope != null)
				CurScope.Dispose();

			CurScope = FHost.Services.CreateScope();
		}

		public T GetRequiredService<T>()
		{
			Type TType = typeof(T);

			if (TType.IsClass)
			{

				Type[] Interfaces = TType.GetInterfaces();

				if (Interfaces.Length == 1)
					return (T)CurScope.ServiceProvider.GetRequiredService(Interfaces[0]);


			}
			else if (TType.IsInterface)
			{
				return (T)CurScope.ServiceProvider.GetRequiredService(TType);
			}

			throw new NotImplementedException();
		}
	}
}
