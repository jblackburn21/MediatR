namespace MediatR
{
    using Internal;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Default mediator implementation relying on single- and multi instance delegates for resolving handlers.
    /// </summary>
    public class Mediator : IMediator
    {
        private readonly SingleInstanceFactory _singleInstanceFactory;
        private readonly MultiInstanceFactory _multiInstanceFactory;
        private static readonly ConcurrentDictionary<Type, RequestHandler> _voidRequestHandlers = new ConcurrentDictionary<Type, RequestHandler>();
        private static readonly ConcurrentDictionary<Type, object> _requestHandlers = new ConcurrentDictionary<Type, object>();

        private readonly ConcurrentDictionary<Type, Type> _genericHandlerCache;
        private readonly ConcurrentDictionary<Type, Type> _wrapperHandlerCache;


        /// <summary>
        /// Initializes a new instance of the <see cref="Mediator"/> class.
        /// </summary>
        /// <param name="singleInstanceFactory">The single instance factory.</param>
        /// <param name="multiInstanceFactory">The multi instance factory.</param>
        public Mediator(SingleInstanceFactory singleInstanceFactory, MultiInstanceFactory multiInstanceFactory)
        {
            _singleInstanceFactory = singleInstanceFactory;
            _multiInstanceFactory = multiInstanceFactory;

            _genericHandlerCache = new ConcurrentDictionary<Type, Type>();
            _wrapperHandlerCache = new ConcurrentDictionary<Type, Type>();
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestType = request.GetType();

            var handler = (RequestHandler<TResponse>)_requestHandlers.GetOrAdd(requestType,
                t => Activator.CreateInstance(typeof(RequestHandlerImpl<,>).MakeGenericType(requestType, typeof(TResponse))));

            return handler.Handle(request, cancellationToken, _singleInstanceFactory, _multiInstanceFactory);
        }

        public Task Send(IRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestType = request.GetType();

            var handler = _voidRequestHandlers.GetOrAdd(requestType,
                t => (RequestHandler) Activator.CreateInstance(typeof(RequestHandlerImpl<>).MakeGenericType(requestType)));

            return handler.Handle(request, cancellationToken, _singleInstanceFactory, _multiInstanceFactory);
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default(CancellationToken))
            where TNotification : INotification
        {
            var notificationHandlers = GetNotificationHandlers(notification)
                .Select(handler =>
                {
                    handler.Handle(notification);
                    return Unit.Task;
                });

            var asyncNotificationHandlers = GetAsyncNotificationHandlers(notification)
                .Select(handler => handler.Handle(notification));

            var cancellableAsyncNotificationHandlers = GetCancellableAsyncNotificationHandlers(notification)
                .Select(handler => handler.Handle(notification, cancellationToken));

            var allHandlers = notificationHandlers
                .Concat(asyncNotificationHandlers)
                .Concat(cancellableAsyncNotificationHandlers);

            return PublishCore(allHandlers);
        }

        /// <summary>
        /// Override in a derived class to control how the tasks are awaited. By default the implementation is <see cref="Task.WhenAll(IEnumerable{Task})" />
        /// </summary>
        /// <param name="allHandlers">Enumerable of tasks representing invoking each notification handler</param>
        /// <returns>A task representing invoking all handlers</returns>
        protected virtual Task PublishCore(IEnumerable<Task> allHandlers)
        {
            return Task.WhenAll(allHandlers);
        }

        private IEnumerable<NotificationHandlerWrapper> GetNotificationHandlers(INotification notification)
        {
            return GetNotificationHandlers<NotificationHandlerWrapper>(notification,
                typeof(INotificationHandler<>),
                typeof(NotificationHandlerWrapper<>));
        }

        private IEnumerable<AsyncNotificationHandlerWrapper> GetAsyncNotificationHandlers(INotification notification)
        {
            return GetNotificationHandlers<AsyncNotificationHandlerWrapper>(notification,
                typeof(IAsyncNotificationHandler<>),
                typeof(AsyncNotificationHandlerWrapper<>));
        }

        private IEnumerable<CancellableAsyncNotificationHandlerWrapper> GetCancellableAsyncNotificationHandlers(INotification notification)
        {
            return GetNotificationHandlers<CancellableAsyncNotificationHandlerWrapper>(notification,
                typeof(ICancellableAsyncNotificationHandler<>),
                typeof(CancellableAsyncNotificationHandlerWrapper<>));
        }

        private IEnumerable<TWrapper> GetNotificationHandlers<TWrapper>(object notification, Type handlerType, Type wrapperType)
        {
            var notificationType = notification.GetType();

            var genericHandlerType = handlerType.MakeGenericType(notificationType);// _genericHandlerCache.GetOrAdd(notificationType, handlerType, (type, root) => root.MakeGenericType(type));
            var genericWrapperType = wrapperType.MakeGenericType(notificationType);//_wrapperHandlerCache.GetOrAdd(notificationType, wrapperType, (type, root) => root.MakeGenericType(type)));

            return GetNotificationHandlers(notification, genericHandlerType)
                .Select(handler => Activator.CreateInstance(genericWrapperType, handler))
                .Cast<TWrapper>()
                .ToList();
        }

        private IEnumerable<object> GetNotificationHandlers(object notification, Type handlerType)
        {
            try
            {
                return _multiInstanceFactory(handlerType);
            }
            catch (Exception e)
            {
                //throw BuildException(notification, e);
                throw;
            }
        }

    }
}
