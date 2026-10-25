using Rge.BuildingBlocks.SharedKernel.Primitives;

using Rge.Identity.Application.ActAs;

using Rge.Identity.Application.CreateCsrfToken;

using Rge.Identity.Application.GetSession;

using Rge.Identity.Application.ListFinanceUsers;

using Rge.Identity.Application.Login;

using Rge.Identity.Application.Logout;

using Rge.Identity.Application.OAuthBegin;

using Rge.Identity.Application.OAuthCallback;

using Rge.Identity.Application.Ports;

using Rge.Identity.Application.ResolveActor;

using Rge.Identity.Application.SwitchUser;



namespace Rge.Identity.Infrastructure;



/// <summary>

/// Provides composition root wiring for the Identity module's infrastructure adapters and application handlers.

/// </summary>

public sealed class IdentityModuleServices

{

    public IAuthenticator Authenticator { get; }

    public IOAuthStateStore OAuthStateStore { get; }

    public IRoleStore RoleStore { get; }

    public IActorCache ActorCache { get; }

    public ICsrfTokenService CsrfTokenService { get; }



    public LoginHandler LoginHandler { get; }

    public LogoutHandler LogoutHandler { get; }

    public GetSessionHandler GetSessionHandler { get; }

    public CreateCsrfTokenHandler CreateCsrfTokenHandler { get; }

    public OAuthBeginHandler OAuthBeginHandler { get; }

    public OAuthCallbackHandler OAuthCallbackHandler { get; }

    public ResolveActorHandler ResolveActorHandler { get; }

    public ListFinanceUsersHandler ListFinanceUsersHandler { get; }

    public SwitchUserHandler SwitchUserHandler { get; }

    public ActAsHandler ActAsHandler { get; }



    private IdentityModuleServices(

        IAuthenticator authenticator,

        IOAuthStateStore oauthStateStore,

        IRoleStore roleStore,

        IActorCache actorCache,

        ICsrfTokenService csrfTokenService,

        IClock clock)

    {

        Authenticator = authenticator;

        OAuthStateStore = oauthStateStore;

        RoleStore = roleStore;

        ActorCache = actorCache;

        CsrfTokenService = csrfTokenService;



        LoginHandler = new LoginHandler(authenticator, roleStore, actorCache, csrfTokenService, clock);

        LogoutHandler = new LogoutHandler(authenticator, clock);

        GetSessionHandler = new GetSessionHandler(authenticator, roleStore, actorCache, clock);

        CreateCsrfTokenHandler = new CreateCsrfTokenHandler(csrfTokenService, clock);

        OAuthBeginHandler = new OAuthBeginHandler(oauthStateStore);

        OAuthCallbackHandler = new OAuthCallbackHandler(oauthStateStore);

        ResolveActorHandler = new ResolveActorHandler(roleStore, actorCache);

        ListFinanceUsersHandler = new ListFinanceUsersHandler(roleStore);

        SwitchUserHandler = new SwitchUserHandler(authenticator, roleStore, actorCache, clock);

        ActAsHandler = new ActAsHandler(authenticator, clock);

    }



    public static IdentityModuleServices Create(

        IIdentityDbConnectionFactory connectionFactory,

        IClock clock,

        byte[] csrfSigningKey,

        TimeSpan? actorCacheTtl = null)

    {

        ArgumentNullException.ThrowIfNull(connectionFactory);

        ArgumentNullException.ThrowIfNull(clock);

        ArgumentNullException.ThrowIfNull(csrfSigningKey);



        var authenticator = new PgAuthenticator(connectionFactory);

        var oauthStateStore = new PgOAuthStateStore(connectionFactory);

        var roleStore = new RoleStore(connectionFactory);

        var actorCache = new ActorCache(clock, actorCacheTtl);

        var csrfTokenService = new CsrfTokenService(csrfSigningKey);



        return new IdentityModuleServices(

            authenticator,

            oauthStateStore,

            roleStore,

            actorCache,

            csrfTokenService,

            clock);

    }

}
