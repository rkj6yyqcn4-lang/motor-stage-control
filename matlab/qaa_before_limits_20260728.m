clc; clear;

%% =========================================================
% 一维位移台 PWM 连续驱动程序
% 接线：
%   PWM1    -> 驱动器 PUL+
%   OUTPUT3 -> 驱动器 DIR+
%   GND     -> 驱动器 PUL- 和 DIR-
%
% 功能：
%   直接改下面参数即可控制：
%   1. 开始运动时间
%   2. 运动方向
%   3. 运动速度，也就是 PWM 频率
%   4. 运动时间
% =========================================================

%% ================== 1. 用户参数区 ==================

dllPath = 'USBDAQ_DLL_V12X64.dll';
hPath   = 'HKDAQ_MATLAB.h';
libName = 'USBDAQ_DLL_V12X64';

PWM_CH = 1;       % PWM1
DIR_CH = 2;       % OUTPUT3

start_delay_s = 0;     % 启动延时，单位 s
move_dir      = 0;     % 方向：1 正向，0 反向
freq_Hz       = 100;  % PWM频率，单位 Hz，频率越高速度越快
move_time_s   = 5;     % 运动时间，单位 s
duty_percent  = 50;    % 占空比，步进驱动器一般用 50%

dir_reverse = false;   % 如果实际运动方向反了，改成 true

%% ================== 2. 参数保护 ==================

if freq_Hz <= 0
    error('freq_Hz 必须大于 0');
end

if move_time_s <= 0
    error('move_time_s 必须大于 0');
end

if duty_percent < 1 || duty_percent > 99
    error('duty_percent 建议设置在 1~99 之间，常用 50');
end

if dir_reverse
    move_dir = 1 - move_dir;
end

%% ================== 3. 加载 DLL ==================

if ~libisloaded(libName)
    loadlibrary(dllPath, hPath, 'alias', libName);
end

fprintf('DLL 加载成功\n');

%% ================== 4. 打开设备 ==================

ret = calllib(libName, 'OpenUsbV12');

if ret ~= 0
    error('OpenUsbV12 打开失败，返回值 = %d', ret);
end

fprintf('采集卡打开成功\n');

% 出错或中断时自动停止 PWM 并关闭设备
cleanObj = onCleanup(@() safe_close(libName, PWM_CH));

%% ================== 5. 设置方向 ==================

ret = calllib(libName, 'DoSetV12', uint8(DIR_CH), uint8(move_dir));

if ret ~= 0
    error('OUTPUT%d 方向设置失败，返回值 = %d', DIR_CH, ret);
end

if move_dir == 1
    fprintf('方向：正向，OUTPUT%d = 高电平\n', DIR_CH);
else
    fprintf('方向：反向，OUTPUT%d = 低电平\n', DIR_CH);
end

%% ================== 6. 延时启动 ==================

if start_delay_s > 0
    fprintf('延时 %.3f s 后开始运动\n', start_delay_s);
    pause(start_delay_s);
end

%% ================== 7. 启动 PWM1 连续输出 ==================

fprintf('开始运动：PWM%d，频率 = %.1f Hz，时间 = %.3f s\n', ...
    PWM_CH, freq_Hz, move_time_s);

ret = calllib(libName, 'PWMOutSetV12', ...
    uint8(PWM_CH), uint32(freq_Hz), single(duty_percent));

if ret ~= 0
    error('PWM%d 启动失败，返回值 = %d', PWM_CH, ret);
end

pause(move_time_s);

%% ================== 8. 停止 PWM ==================

ret = calllib(libName, 'PWMOutSetV12', ...
    uint8(PWM_CH), uint32(freq_Hz), single(0));

if ret ~= 0
    warning('PWM%d 停止时返回值 = %d', PWM_CH, ret);
else
    fprintf('PWM%d 已停止\n', PWM_CH);
end

%% ================== 9. 关闭设备 ==================

calllib(libName, 'CloseUsbV12');
fprintf('采集卡已关闭\n');

%% ================== 10. 可选：卸载 DLL ==================
% 如果后面还要继续运行程序，可以不卸载
% unloadlibrary(libName);


%% =========================================================
% 安全关闭函数
% =========================================================
function safe_close(libName, pwm_ch)

    try
        if libisloaded(libName)
            % 停止 PWM
            try
                calllib(libName, 'PWMOutSetV12', ...
                    uint8(pwm_ch), uint32(1000), single(0));
            catch
            end

            % 关闭设备
            try
                calllib(libName, 'CloseUsbV12');
            catch
            end
        end
    catch
    end

end