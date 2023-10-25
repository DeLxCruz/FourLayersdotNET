using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using API.Dtos;
using API.Helpers;
using Domain.Entities;
using Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace API.Services
{
    public class UserService : IUserService
    {
        private readonly JWT _jwt;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher<User> _passwordHasher;
        public UserService(IUnitOfWork unitOfWork, IOptions<JWT> jwt, IPasswordHasher<User> passwordHasher)
        {
            _jwt = jwt.Value;
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
        }
        public async Task<string> RegisterAsync(RegisterDto registerDto)
        {
            var user = new User
            {
                Email = registerDto.Email,
                Username = registerDto.Username
            };

            user.Password = _passwordHasher.HashPassword(user, registerDto.Password); //Encrypt password

            var existingUser = _unitOfWork.Users
                                        .Find(u => u.Username.ToLower() == registerDto.Username.ToLower())
                                        .FirstOrDefault();

            if (existingUser == null)
            {
                var rolDefault = _unitOfWork.Roles
                                        .Find(u => u.Nombre == Authorization.rol_default.ToString())
                                        .First();
                try
                {
                    user.Rols.Add(rolDefault);
                    _unitOfWork.Users.Add(user);
                    await _unitOfWork.SaveAsync();

                    return $"User {registerDto.Username} has been registered successfully.";
                }
                catch (Exception ex)
                {
                    var message = ex.Message;
                    return $"Error: {message}";
                }
            }
            else
            {
                return $"User {registerDto.Username} already registered.";
            }
        }

        public async Task<DataUserDto> GetTokenAsync(LoginDto model)
        {
            DataUserDto dataUserDto = new DataUserDto();
            var user = await _unitOfWork.Users.GetByUsernameAsync(model.Username);

            if (user == null)
            {
                dataUserDto.IsAuthenticated = false;
                dataUserDto.Message = $"User {model.Username} not found.";
                return dataUserDto;
            }

            var result = _passwordHasher.VerifyHashedPassword(user, user.Password, model.Password);

            if (result == PasswordVerificationResult.Success)
            {
                dataUserDto.IsAuthenticated = true;
                JwtSecurityToken jwtSecurityToken = CreateJwtToken(user);
                dataUserDto.Token = new JwtSecurityTokenHandler().WriteToken(jwtSecurityToken);
                dataUserDto.Email = user.Email;
                dataUserDto.Username = user.Username;
                dataUserDto.Roles = user.Rols.Select(r => r.Nombre).ToList();

                if (user.RefreshTokens.Any(a => a.IsActive))
                {
                    var activeRefreshToken = user.RefreshTokens.Where(a => a.IsActive == true).FirstOrDefault();
                    dataUserDto.RefreshToken = activeRefreshToken.Token;
                    dataUserDto.RefreshTokenExpiration = activeRefreshToken.Expires;
                }
                else
                {
                    var refreshToken = CreateRefreshToken();
                    dataUserDto.RefreshToken = refreshToken.Token;
                    dataUserDto.RefreshTokenExpiration = refreshToken.Expires;
                    user.RefreshTokens.Add(refreshToken);
                    _unitOfWork.Users.Update(user);
                    await _unitOfWork.SaveAsync();
                }
                return dataUserDto;
            }
            dataUserDto.IsAuthenticated = false;
            dataUserDto.Message = $"Incorrect credentials for user {model.Username}.";
            return dataUserDto;
        }

        public async Task<string> AddRoleAsync(AddRoleDto model)
        {
            var user = await _unitOfWork.Users.GetByUsernameAsync(model.Username);

            if (user == null)
            {
                return $"User {model.Username} does not exists.";
            }

            var result = _passwordHasher.VerifyHashedPassword(user, user.Password, model.Password);

            if (result == PasswordVerificationResult.Success)
            {
                var rolExists = await _unitOfWork.Roles.Find(r => r.Nombre.ToLower() == model.Role.ToLower()).FirstOrDefault();

                if (rolExists != null)
                {
                    var userHasRole = user.Rols.Any(u => u.Id == rolExists.Id);

                    if (userHasRole == false)
                    {
                        user.Rols.Add(rolExists);
                        _unitOfWork.Users.Update(user);
                        await _unitOfWork.SaveAsync();
                    }

                    return $"User {model.Username} has been assigned to role {model.Role}.";
                }
                return $"Role {model.Role} was not found.";
            }
            return $"Incorrect credentials for user {model.Username}.";
        }

        public async Task<DataUserDto> RefreshTokenAsync(string refreshToken)
        {
            var dataUserDto = new DataUserDto();
            var user = await _unitOfWork.Users.GetRefreshTokenAsync(refreshToken);

            if (user == null)
            {
                dataUserDto.IsAuthenticated = false;
                dataUserDto.Message = $"Token is not assigned to any user.";
                return dataUserDto;
            }

            var refreshTokenBd = user.RefreshTokens.Single(x => x.Token == refreshToken);

            if (!refreshTokenBd.IsActive)
            {
                dataUserDto.IsAuthenticated = false;
                dataUserDto.Message = $"Token is not active.";
                return dataUserDto;
            }

            //Revoque the current refresh token and generate a new one
            refreshTokenBd.Revoked = DateTime.UtcNow;

            //Generate a new refresh token and save to database
            var newRefreshToken = CreateRefreshToken();
            user.RefreshTokens.Add(newRefreshToken);
            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveAsync();

            //Generate a new JWT
            dataUserDto.IsAuthenticated = true;
            JwtSecurityToken jwtSecurityToken = CreateJwtToken(user);
            dataUserDto.Token = new JwtSecurityTokenHandler().WriteToken(jwtSecurityToken);
            dataUserDto.Email = user.Email;
            dataUserDto.Username = user.Username;
            dataUserDto.Roles = user.Rols.Select(r => r.Nombre).ToList();
            dataUserDto.RefreshToken = newRefreshToken.Token;
            dataUserDto.RefreshTokenExpiration = newRefreshToken.Expires;
            return dataUserDto;
        }

        private JwtSecurityToken CreateJwtToken(User user)
        {
            var roles  = user.Rols;
            var roleClaims = new List<Claim>();
            foreach (var role in roles)
            {
                roleClaims.Add(new Claim("roles", role.Nombre));
            }
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), //Jti: Unique identifier for the token
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("uid", user.Id.ToString())
            }
            .Union(roleClaims);
            
            var symmetricSecurityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));
            var signingCredentials = new SigningCredentials(symmetricSecurityKey, SecurityAlgorithms.HmacSha256);
            var jwtSecurityToken = new JwtSecurityToken(
                issuer: _jwt.Issuer,
                audience: _jwt.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_jwt.DurationInMinutes),
                signingCredentials: signingCredentials
            );
            return jwtSecurityToken;
        }

        private RefreshToken CreateRefreshToken()
        {
            var randomNumber = new byte[32];
            using (var generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(randomNumber);
                return new RefreshToken
                {
                    Token = Convert.ToBase64String(randomNumber),
                    Expires = DateTime.UtcNow.AddMinutes(10),
                    Created = DateTime.UtcNow
                };
            }
        }
    }
}